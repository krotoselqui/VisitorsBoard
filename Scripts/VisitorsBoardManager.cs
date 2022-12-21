using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class VisitorsBoardManager : UdonSharpBehaviour
{
    //INSPECTOR
    [Header("訪問者の名前の色")]
    [SerializeField] private Color cl_inworld;
    private string cl_inworld_code = "#FFFFFF";

    [Header("訪問者の名前の色 (退室後)")]
    [SerializeField] private Color cl_absent;
    private string cl_absent_code = "#666666";

    [Header("入退室時刻を表示への切り替えを有効にする")]
    //[Header("   ...在室表示 → 全員表示 → 在室表示... の切り替えがデフォルトですが")]
    //[Header("   ...在室表示 → 全員表示 → 入退室時間表示 → 在室表示... の順切り替えになります")]
    [SerializeField] private bool enableEntryExitTimeView = false;

    [Header("インスタンス経過時間を表示しない")]
    [SerializeField] private bool dontShowElapsedTime = false;

    [Header("最新の入室時間を入室時間として使う")]
    [SerializeField] private bool useNewestJoinTime = true;

    [Header("１列に入る人数")]
    [SerializeField, Range(1, 100)] private int name_per_object = 20;

    [Header("Lastline Overflow(beta)")]
    [SerializeField, Range(0, 1000)] private int lastline_overflow = 200;
    
    [Header("Object Slots")]
    [SerializeField] private Text time_text = null;
    [SerializeField] private Text elapsed_text = null;
    [SerializeField] private Text visitorsnumber_text = null;
    [SerializeField] private Text[] name_text = null;

    [Header("Date Format - Header")]
    [SerializeField] private string format_date = "yyyy-MM-dd  HH:mm:ss";

    [Header("Time Format - Member")]
    [SerializeField] private string format_time = "HH:mm";

    [Header("World Capacity")]
    [SerializeField] private int world_capacity = 64;

    //SYNCED 
    [UdonSynced] private long time_created = 0;
    [UdonSynced] private string[] visitor_names = null;
    [UdonSynced] private string[] st_firstVisitTime = null;
    [UdonSynced] private string[] st_exitTime = null;

    //INTERNAL
    private int current_viewstat = 0;

    private int small_fontsize_day = 40;
    private int small_fontsize_membercount = 30;
    private int small_fontsize_item = 10;

    private int namecount_MAX = 1;

    private int cur_sec = 0;
    private int prev_sec = 0;
    VRCPlayerApi[] players = null;

    private int late_update_counter = 2;

    //CONSTANTS
    private const int STAT_NUM_OFFSET = 90;
    private const int STAT_INWORLD_VIEW = 90;
    private const int STAT_ALL_VIEW = 91;
    private const int STAT_SHOW_TIME = 92;
    private const int STAT_RESERVED_01 = 93;
    private const int STAT_RESERVED_02 = 94;

    private const string SYNC_WAIT_MESSAGE = "Waiting for sync...";

    //UNITY EVENTS
    private void Start()
    {
        VRCPlayerApi[] players = new VRCPlayerApi[world_capacity * 2];
        namecount_MAX = (name_text.Length * name_per_object > 0) ? name_text.Length * name_per_object + lastline_overflow : 1;

        if (elapsed_text != null) small_fontsize_day = (int)(elapsed_text.fontSize * 0.4f) + 1;
        if (visitorsnumber_text != null) small_fontsize_membercount = (int)(visitorsnumber_text.fontSize * 0.6f) + 1;
        if (name_text[0] != null) small_fontsize_item = (int)(name_text[0].fontSize * 0.5f) + 1;

        cl_inworld_code = StrRGBofColor(cl_inworld);
        cl_absent_code = StrRGBofColor(cl_absent);

        for (int i = 0; i < name_text.Length; i++)
        {
            if (name_text[i] == null) continue;
            if (name_per_object > 1)
            {
                name_text[i].alignment = TextAnchor.UpperCenter;
                name_text[i].verticalOverflow = VerticalWrapMode.Overflow;
                name_text[i].horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        current_viewstat = STAT_INWORLD_VIEW;
        time_text.text = SYNC_WAIT_MESSAGE;

        if (Networking.IsOwner(this.gameObject))
        {
            InitializeInformation();
        }
    }

    private void Update()
    {
        if (visitor_names != null && st_firstVisitTime != null && st_exitTime != null)
        {
            cur_sec = DateTime.Now.Second;
            if (prev_sec != cur_sec)
            {
                prev_sec = cur_sec;
                DisplayElapsedTimeOfInstance();

                if (late_update_counter > 0)
                {
                    if (late_update_counter == 1) DisplayVisitorBoard();
                    late_update_counter--;
                }
            }
        }
    }

    public override void OnDeserialization()
    {
        if (visitor_names != null && st_firstVisitTime != null && st_exitTime != null)
        {
            DisplayVisitorBoard();
        }
        else
        {
            //InitializeInformation();
            return;
        }
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        late_update_counter = 3;
        if (visitor_names != null && st_firstVisitTime != null && st_exitTime != null)
        {
            if (Networking.IsOwner(this.gameObject))
            {
                AddNameAndJoinStamp(player.displayName);
                DisplayVisitorBoard();
            }
        }
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        late_update_counter = 3;
        if (visitor_names != null && st_firstVisitTime != null && st_exitTime != null)
        {
            if (Networking.IsOwner(this.gameObject))
            {
                AddExitStamp(player.displayName);
                DisplayVisitorBoard();
            }
        }
    }

    public override void Interact()
    {
        SwitchStatToNext();
        if (visitor_names != null && st_firstVisitTime != null && st_exitTime != null) DisplayVisitorBoard();
    }

    public void OnPickUpUseDown()
    {
        SwitchStatToNext();
        if (visitor_names != null && st_firstVisitTime != null && st_exitTime != null) DisplayVisitorBoard();
    }

    //CUSTOM EVENTS
    private void InitializeInformation()
    {
        Networking.SetOwner(Networking.LocalPlayer, this.gameObject);

        time_created = DateTime.UtcNow.Ticks;
        visitor_names = new string[namecount_MAX];
        st_firstVisitTime = new string[namecount_MAX];
        st_exitTime = new string[namecount_MAX];

        for (int i = 0; i < visitor_names.Length; i++)
        {
            visitor_names[i] = string.Empty;
            st_firstVisitTime[i] = string.Empty;
            st_exitTime[i] = string.Empty;
        }

        RequestSerialization();
    }

    private void SwitchStatToNext()
    {
        current_viewstat = enableEntryExitTimeView ?
         (current_viewstat - STAT_NUM_OFFSET + 1) % 3 + STAT_NUM_OFFSET :
         (current_viewstat - STAT_NUM_OFFSET + 1) % 2 + STAT_NUM_OFFSET;
    }

    private void AddNameAndJoinStamp(string name)
    {
        for (int i = 0; i < visitor_names.Length; i++)
        {
            if (string.IsNullOrEmpty(visitor_names[i]))
            {
                visitor_names[i] = name;
                st_firstVisitTime[i] = CurrentDateTimeString(format_time);
                st_exitTime[i] = "";
                break;
            }
            else if (visitor_names[i] == name)
            {
                if (useNewestJoinTime)
                {
                    st_firstVisitTime[i] = CurrentDateTimeString(format_time);
                }
                break;
            }
        }
        RequestSerialization();
    }

    private void AddExitStamp(string name)
    {
        for (int i = 0; i < visitor_names.Length; i++)
        {
            if (visitor_names[i] == name)
            {
                st_exitTime[i] = CurrentDateTimeString(format_time);
                break;
            }
        }
        RequestSerialization();
    }

    private void DisplayElapsedTimeOfInstance()
    {
        if (dontShowElapsedTime)
        {
            elapsed_text.text = "[Disabled]";
            return;
        }

        long current_tick = DateTime.UtcNow.Ticks;
        TimeSpan ts = new TimeSpan(current_tick - time_created);

        String tx_elapsed = "";
        tx_elapsed += ts.TotalHours < 24 ? "" : ts.Days.ToString("0")
            + SizeFixedString(" DAY(s)", small_fontsize_day);
        tx_elapsed += ts.TotalMinutes < 60 ? "" : ts.Hours.ToString("00") + ":";
        tx_elapsed += ts.TotalSeconds < 60 ? "" : ts.Minutes.ToString("00") + ":";
        tx_elapsed += ts.Seconds.ToString("00");

        elapsed_text.text = tx_elapsed;
    }

    private void DisplayVisitorBoard()
    {
        int visitor_count = 0;
        int inworld_count = 0;
        string[] st_names_inworld = new string[namecount_MAX];
        bool[] is_valid_member = new bool[namecount_MAX];

        //有効名リスト
        players = new VRCPlayerApi[world_capacity * 2];
        VRCPlayerApi.GetPlayers(players);
        foreach (VRCPlayerApi p in players)
        {
            if (p != null) AddStringToArr(st_names_inworld, p.displayName, false);
        }

        //名前チェック
        //inworld_count は GetPlayerCountとは異なる
        for (int k = 0; k < visitor_names.Length; k++)
        {
            is_valid_member[k] = false;
            for (int i = 0; i < st_names_inworld.Length; i++)
            {
                if (string.IsNullOrEmpty(visitor_names[k]))
                {
                    continue;
                }
                else if (visitor_names[k] == st_names_inworld[i])
                {
                    is_valid_member[k] = true;
                    inworld_count++;
                    break;
                }
            }
        }

        //訪問者項目表示
        int cur_obj_index = 0;
        for (int i = 0; i < name_text.Length; i++)
        {
            if (name_text[i] == null) continue;

            int nmIndex_LoopStart = cur_obj_index * name_per_object;
            int nmIndex_LoopEnd = (cur_obj_index + 1) * name_per_object;
            if (i == name_text.Length - 1) nmIndex_LoopEnd += lastline_overflow;

            string st_this_row = "";
            for (int nmIndex = nmIndex_LoopStart; nmIndex < nmIndex_LoopEnd; nmIndex++)
            {
                if (nmIndex >= visitor_names.Length) continue;
                if (string.IsNullOrEmpty(visitor_names[nmIndex])) continue;

                st_this_row += StringItemOfVisitor(nmIndex, is_valid_member[nmIndex]) + "\n";

                visitor_count++;
            }

            name_text[i].text = st_this_row;
            cur_obj_index++;
        }

        //インスタンス作成時刻表示
        if (time_text.text == SYNC_WAIT_MESSAGE)
        {
            time_text.text = CurrentDateTimeString(format_date);
        }

        //訪問者人数表示
        string str_visitor_count = inworld_count.ToString("00");
        //str_visitor_count += inworld_count > namecount_MAX ? "+" : "";



        //string str_visitor_total_count = $"{visitor_count}";
        // string str_visitor_total_count = visitor_count.ToString("00");
        // if (visitor_count+1 >= namecount_MAX) str_visitor_total_count = "MAX";

        if (current_viewstat != STAT_ALL_VIEW)
            str_visitor_count += SizeFixedString(" / " + str_visitor_total_count, small_fontsize_membercount);

        visitorsnumber_text.text = str_visitor_count;
    }

    private string StringItemOfVisitor(int index, bool valid)
    {
        String st = "";

        if (current_viewstat == STAT_INWORLD_VIEW)
        {
            st = VisitorNameString(valid, visitor_names[index]);
        }
        else if (current_viewstat == STAT_ALL_VIEW)
        {
            st = VisitorNameString(true, visitor_names[index]);
        }
        else if (current_viewstat == STAT_SHOW_TIME)
        {
            st = VisitorNameString(valid, visitor_names[index], st_firstVisitTime[index], st_exitTime[index]);
        }
        else
        {
            st = "-invalid state-";
        }

        return st;
    }

    private int AddStringToArr(ref string[] arr,string st,bool allowDupl = true)
    {
        if(!allowDupl){
            foreach(st s in arr) if(s == st)return -1;
        }

        for(int i=0;i<arr.Length;i++)
        {
            if (string.IsNullOrEmpty(arr[i]))
            {
                arr[i] = st;
                return i;
            } 
            else if(i == arr.Length -1) 
            {
                Array.Resize(ref arr,arr.Length+1);
                arr[i+1]=st;
                return i+1;
            }
        }

        return -1;
    }

    private string VisitorNameString(bool valid, string name) => ColorFixedString(name, valid ? cl_inworld_code : cl_absent_code);
    private string VisitorNameString(bool valid, string name, string joinTimeSt, string ExitTimeSt)
    {
        string st_time = valid ?
             "[ " + joinTimeSt + " - ]" : "[ " + joinTimeSt + " - " + ExitTimeSt + "]";
        return ColorFixedString(name + SizeFixedString(st_time, small_fontsize_item), valid ? cl_inworld_code : cl_absent_code);
    }
    private string ColorFixedString(string st, string colorCode) => "<color=" + colorCode + ">" + st + "</color>";
    private string SizeFixedString(string st, int siz) => "<size=" + siz.ToString() + ">" + st + "</size>";
    private string CurrentDateTimeString(string format) =>
        $"{new DateTime(DateTime.UtcNow.Ticks).ToLocalTime().ToString(format, CultureInfo.InvariantCulture)}";
    private string StrRGBofColor(Color clr) =>
        $"#{(int)(clr.r * 255):X2}{(int)(clr.g * 255):X2}{(int)(clr.b * 255):X2}";

    private long OldestVisitorTick()
    {
        //nullは現在時刻を返す
        long tick = DateTime.Now.Ticks;
        // if (visitor_names != null)
        // {
        //     for (int i = 0; i < visitor_names.Length; i++)
        //     {
        //         if (firstVisitTicks[i] == 0) continue;
        //         if (firstVisitTicks[i] < tick)
        //         {
        //             tick = firstVisitTicks[i];
        //         }
        //     }
        // }

        return tick;
    }

}
