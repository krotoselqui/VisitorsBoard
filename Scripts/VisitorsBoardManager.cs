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
    [Header("訪問者名カラーコード(在室 / #省略可)")]
    [SerializeField] private string cl_inworld_code = "#FFFFFF";

    [Header("訪問者名カラーコード(退室後 / #省略可)")]
    [SerializeField] private string cl_absent_code = "#666666";

    //ColorUtilityがない
    // [SerializeField]private Color cl_inworld;
    // [SerializeField]private Color cl_absent;
    //ColorUtility.ToHtmlStringRGB(cl_inworld);
    //ColorUtility.ToHtmlStringRGB(cl_absent);
    
    [Header("入退室時刻を表示への切り替えを有効にする")]
    [Header("   ...在室表示 → 全員表示 → 在室表示... の切り替えがデフォルトですが")]
    [Header("   ...在室表示 → 全員表示 → 入退室時間表示 → 在室表示... の順切り替えになります")]
    [SerializeField] private bool enableEntryExitTimeView = false;

    [Header("インスタンス経過時間を表示しない")]
    [SerializeField] private bool dontShowElapsedTime = false;

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

//SYNCED 
    [UdonSynced] private long time_created = 0;
    [UdonSynced] private string[] visitor_names = null;
    [UdonSynced] private string[] st_firstVisitTime = null;
    [UdonSynced] private string[] st_exitTime = null;

//INTERNAL
    private int current_viewstat = 0;

    private int small_fontsize_day = 40;
    private int small_fontsize_membercount = 30;

    private int namecount_MAX = 1;
    private int playersId_MAX = 10000; 

    private int cur_sec = 0;
    private int prev_sec = 0;

//CONSTANTS
    private const int STAT_INWORLD_VIEW = 90;
    private const int STAT_ALL_VIEW = 91;
    private const int STAT_SHOW_TIME = 92;
    private const int STAT_RESERVED_01 = 93;
    private const int STAT_RESERVED_02 = 94;

    private const string SYNC_WAIT_MESSAGE = "Waiting for sync...";

//UNITY EVENTS
    private void Start()
    {
        namecount_MAX = (name_text.Length * name_per_object > 0 ) ? name_text.Length * name_per_object + lastline_overflow : 1;

        if (elapsed_text != null) small_fontsize_day = (int)(elapsed_text.fontSize * 0.4f) + 1;
        if (visitorsnumber_text != null) small_fontsize_membercount = (int)(visitorsnumber_text.fontSize * 0.6f) + 1;
        cl_inworld_code = cl_inworld_code.StartsWith("#") ? cl_inworld_code : "#" + cl_inworld_code;
        cl_absent_code = cl_absent_code.StartsWith("#") ? cl_absent_code : "#" + cl_absent_code;

        for(int i=0;i<name_text.Length;i++)
        {
            if (name_text == null) continue;
            if (name_per_object > 1)
            {
                name_text[i].alignment = TextAnchor.UpperCenter;
                name_text[i].verticalOverflow = VerticalWrapMode.Overflow;
                name_text[i].horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        time_text.text = SYNC_WAIT_MESSAGE

        if (Networking.IsOwner(this.gameObject))
        {
            InitializeInformation();
        }
    }

    private void Update()
    {
        if (visitor_names != null)
        {
            cur_sec = DateTime.Now.Second;
            if (prev_sec != cur_sec)
            {
                prev_sec = cur_sec;
                DisplayElapsedTimeOfInstance();
                if (cur_sec % 30 == 0) DisplayVisitorBoard();
            }
        }
        else
        {
            //InitializeInformation();
            return;
        }
    }

    public override void OnDeserialization()
    {
        if (visitor_names != null)
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
        if (visitor_names != null)
        {
            if (Networking.IsOwner(this.gameObject))
            {
                AddName(player.displayName);
                DisplayVisitorBoard();
            }
        }
        else
        {
            //InitializeInformation();
            return;
        }
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (visitor_names != null)
        {
            if (Networking.IsOwner(this.gameObject))
            {
                AddExitStamp(player.displayName);
                DisplayVisitorBoard();
            }
        }
        else
        {
            //InitializeInformation();
            return;
        }
    }

    public override void Interact()
    {
        current_viewstat = enableEntryExitTimeView ? (current_viewstat + 1) % 3 : (current_viewstat + 1) % 2;
        if (visitor_names != null) DisplayVisitorBoard();
    }

//CUSTOM EVENTS
    private void InitializeInformation()
    {
        Networking.SetOwner(Networking.LocalPlayer, this.gameObject);

        time_created = DateTime.UtcNow.Ticks;
        visitor_names = new string[namecount_MAX];
        firstVisitTicks = new long[namecount_MAX];
        lastVisitTicks = new long[namecount_MAX];

        for (int i = 0; i < visitor_names.Length; i++)
        {
            visitor_names[i] = string.Empty;
        }

        RequestSerialization();
    }

    private void AddName(string name)
    {
        for (int i = 0; i < visitor_names.Length; i++)
        {
            if (string.IsNullOrEmpty(visitor_names[i]))
            {
                visitor_names[i] = name;
                st_firstVisitTime[i] =  $"{new DateTime(DateTime.UtcNow.Ticks).ToLocalTime().ToString(format_time, CultureInfo.InvariantCulture)}";
                break;
            }
            else if (visitor_names[i] == name)
            {
                break;
            }
        }
        RequestSerialization();
    }

    private void AddExitStamp(string name)
    {
        for (int i = 0; i < visitor_names.length;i++)
        { 
            if (visitor_names[i] == name)
            {
                st_exitTime[i] =  $"{new DateTime(DateTime.UtcNow.Ticks).ToLocalTime().ToString(format_time, CultureInfo.InvariantCulture)}";
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
            + "<size=" + small_fontsize_day.ToString() + "> DAY(s)</size>+ ";
        tx_elapsed += ts.TotalMinutes < 60 ? "" : ts.Hours.ToString("00") + ":";
        tx_elapsed += ts.TotalSeconds < 60 ? "" : ts.Minutes.ToString("00") + ":";
        tx_elapsed += ts.Seconds.ToString("00");

        elapsed_text.text = tx_elapsed;
    }

    private void DisplayVisitorBoard()
    {
        int visitor_count = 0;
        int inworld_count = 0;
        string[] st_names_inworld = new string[visitor_names.Length];
        bool[] is_valid_member = new bool[visitor_names.Length];

        //有効名リスト
        int v = 0;
        for (int i = 0; i < playersId_MAX; i++)
        {
            if (v >= visitor_names.length) break;
            var p = VRCPlayerApi.GetPlayerById(i);
            if (p != null)
            {
                st_names_inworld[v] = p.displayName;
                v++;
            }
        }

        //名前チェック
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
            int nmIndex_LoopEnd = nmIndex < (cur_obj_index + 1) * name_per_object;
            if (i == name_text.Length - 1) nmIndex_LoopEnd += lastline_overflow;

            string st_this_row = "";
            for (int nmIndex = nmIndex_LoopStart; nmIndex_LoopEnd; nmIndex++)
            {
                if (nmIndex >= visitor_names.Length) continue;
                if (string.IsNullOrEmpty(visitor_names[nmIndex])) continue;

                st_this_row += StringItemOfVisitor(nmIndex,is_valid_member[nmIndex]) + "\n";

                visitor_count++;
            }

            name_text[i].text = st_this_row;
            cur_obj_index++;
        }

        //インスタンス作成時刻表示
        if(time_text.text == SYNC_WAIT_MESSAGE)
        {
            time_text.text = $"{new DateTime(time_created).ToLocalTime().ToString(format_date, CultureInfo.InvariantCulture)}";
        }

        //訪問者人数表示
        string str_visitor = visitor_count >= namecount_MAX ? visitor_count.ToString("00") + "+" : visitor_count.ToString("00");
        if (current_viewstat != STAT_ALL_VIEW)
        {
            str_visitor += "<size=" + small_fontsize_membercount.ToString() + ">  [CURRENT : " + inworld_count.ToString("00") + "]</size>";
        }
        visitorsnumber_text.text = str_visitor;
    }

    private string StringItemOfVisitor(int visitor_index,bool valid)
    {
        String st = "";

        if(current_viewstat == STAT_INWORLD_VIEW)
        {
            st = valid ? "<color=" + cl_inworld_code + ">" + visitor_names[visitor_index] + "</color>" :
                        "<color=" + cl_absent_code + ">" + visitor_names[visitor_index] + "</color>";
        }
        else if(current_viewstat == STAT_ALL_VIEW)
        {
            st = "<color=" + cl_inworld_code + ">" + visitor_names[visitor_index] + "</color>";
        }
        else if(current_viewstat == STAT_SHOW_TIME)
        {
        //     string first = $"{new DateTime(firstVisitTicks[visitor_index]).ToLocalTime().ToString(format_time, CultureInfo.InvariantCulture)}";
        //     string last = valid ? 
        //     "" :
        //     $"{new DateTime(lastVisitTicks[visitor_index]).ToLocalTime().ToString(format_time, CultureInfo.InvariantCulture)}";
            st = valid ?  $"<color={cl_inworld_code}>[ {st_firstVisitTime[visitor_index]} - ]</color>":
                          $"<color={cl_inworld_code}>[ {st_firstVisitTime[visitor_index]} - {st_exitTime[visitor_index]} ]</color>":
        }
        else
        {
            st = "-invalid state-";
        }

        return st;        
    }

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
