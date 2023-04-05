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
    [SerializeField] private bool enableEntryExitTimeView = false;

    [Header("BASE64確認表示の切り替えを有効にする")]
    [SerializeField] private bool enableBase64View = false;

    [Header("インスタンス経過時間を表示しない")]
    [SerializeField] private bool dontShowElapsedTime = false;

    [Header("最新の入室時間を入室時間として使う")]
    [SerializeField] private bool useNewestJoinTime = true;

    [Header("１列に入る人数")]
    [SerializeField, Range(1, 100)] private int name_per_object = 20;

    [Header("最終行溢れ人数")]
    [SerializeField, Range(0, 1000)] private int last_row_overflow = 200;

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
    [UdonSynced] private long sync_createdTick = 0;
    [UdonSynced] private string[] sync_visitorNames = null;
    // [UdonSynced] private string[] sync_entryTimeSt = null;
    // [UdonSynced] prisync_exitTimevate string[] sync_exitTimeSt = null;
    [UdonSynced] private int[] sync_entryTime = null;
    [UdonSynced] private int[] sync_exitTime = null;

    //INTERNAL
    //INTERNAL
    private int current_stat_index = 0;
    private int[] stats = null;

    private int fontsize_small_day = 40;
    private int fontsize_small_membercount = 30;
    private int fontsize_small_item = 10;

    private int namecount_MAX = 1;

    private int cur_sec = 0;
    private int prev_sec = 0;
    VRCPlayerApi[] players = null;
    VRCPlayerApi localPlayer = null;
    private int retrySyncCount = 0;
    private int retrySyncThres = 60;

    private int late_update_counter = 2;
    private DateTime currentTimeShared = new DateTime();

    //CONSTANTS
    private const int STAT_INWORLD_VIEW = 90;
    private const int STAT_ALL_VIEW = 91;
    private const int STAT_SHOW_TIME = 92;
    private const int STAT_SHOW_POS = 93;
    private const int STAT_BASE64 = 94;
    private const int STAT_ACHIEVE = 95;
    private const int STAT_OPTIONS = 96;

    private const string SYNC_WAIT_MESSAGE = "Waiting for sync...";

    //UNITY EVENTS
    private void Start()
    {
        VRCPlayerApi[] players = new VRCPlayerApi[world_capacity * 2];
        AllocStats();
        current_stat_index = 0;

        localPlayer = Networking.LocalPlayer;

        namecount_MAX = (name_text.Length * name_per_object > 0) ? name_text.Length * name_per_object + last_row_overflow : 1;

        if (elapsed_text != null) fontsize_small_day = GetFontSizeByScaling(elapsed_text, 0.4f);
        if (visitorsnumber_text != null) fontsize_small_membercount = GetFontSizeByScaling(visitorsnumber_text, 0.6f);
        if (name_text != null && name_text[0] != null) fontsize_small_item = GetFontSizeByScaling(name_text[0], 0.5f);

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

        if (time_text != null) time_text.text = SYNC_WAIT_MESSAGE;

        if (Networking.IsOwner(this.gameObject))
        {
            InitializeInformation(true);
        }
        else
        {
            InitializeInformation(false);
        }
    }

    private void Update()
    {
        if (SyncAllValid())
        {
            currentTimeShared = GetUtcNow();
            cur_sec = currentTimeShared.Minute * 60 + currentTimeShared.Second;

            if (prev_sec != cur_sec)
            {
                prev_sec = cur_sec;
                DisplayElapsedTimeOfInstance();

                if (time_text.text == SYNC_WAIT_MESSAGE)
                {
                    retrySyncCount++;
                    if (retrySyncCount > retrySyncThres)
                    {
                        Networking.SetOwner(localPlayer, this.gameObject);
                        InitializeInformation(true);
                        retrySyncCount = 0;
                    }
                }

                if (late_update_counter > 0)
                {
                    if (late_update_counter == 1) DisplayVisitor();
                    late_update_counter--;
                }

                if (cur_sec % 5 == 0)
                {
                    //if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject)) UpdatePositions();
                }


            }
        }
    }

    public override void OnDeserialization()
    {
        if (SyncAllValid())
        {
            DisplayVisitor();
            late_update_counter = 0;
        }
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        late_update_counter = 3;
        if (SyncAllValid())
        {
            if (Networking.IsOwner(this.gameObject))
            {
                AddNameAndJoinStamp(player);
                DisplayVisitor();
            }
        }
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        late_update_counter = 3;
        if (SyncAllValid())
        {
            if (Networking.IsOwner(this.gameObject))
            {
                AddExitStamp(player);
                DisplayVisitor();
            }
        }
    }

    public void OuterInteract() => Interact();
    public override void Interact()
    {
        SwitchStatToNext();
        DisplayVisitor();
    }


    //CUSTOM EVENTS
    private void AllocStats()
    {
        int statcount = 2; // INWORLD + ALL
        int index = statcount;
        if (enableEntryExitTimeView) statcount++;
        //if(enablePositionView) statcount++;
        if (enableBase64View) statcount++;

        stats = new int[statcount];

        stats[0] = STAT_INWORLD_VIEW;
        stats[1] = STAT_ALL_VIEW;

        if (enableEntryExitTimeView) stats[index++] = STAT_SHOW_TIME;
        //if(enablePositionView) stats[index] = STAT_SHOW_POS;
        if (enableBase64View) stats[index++] = STAT_BASE64;
    }

    private void InitializeInformation(bool owner)
    {
        if (owner)
        {
            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);

            sync_createdTick = DateTime.UtcNow.Ticks;
            sync_visitorNames = new string[namecount_MAX];
            sync_entryTime = new int[namecount_MAX];
            sync_exitTime = new int[namecount_MAX];

            for (int i = 0; i < sync_visitorNames.Length; i++)
            {
                sync_visitorNames[i] = string.Empty;
                sync_entryTime[i] = -1;
                sync_exitTime[i] = -1;
            }

            RequestSerialization();
        }
        else //2023-2-20
        {
            sync_createdTick = DateTime.UtcNow.Ticks;
            sync_visitorNames = new string[namecount_MAX];
            sync_entryTime = new int[namecount_MAX];
            sync_exitTime = new int[namecount_MAX];
        }
    }

    private void SwitchStatToNext() => current_stat_index = (current_stat_index + 1) % stats.Length;

    private void AddNameAndJoinStamp(VRCPlayerApi vp)
    {
        if (vp == null) return;
        string name = vp.displayName;

        for (int i = 0; i < sync_visitorNames.Length; i++)
        {
            if (string.IsNullOrEmpty(sync_visitorNames[i]))
            {
                sync_visitorNames[i] = name;
                sync_entryTime[i] = DateTimeToHourMinInt(currentTimeShared.ToLocalTime());
                sync_exitTime[i] = -1;
                break;
            }
            else if (sync_visitorNames[i] == name)
            {
                if (useNewestJoinTime)
                {
                    sync_entryTime[i] = DateTimeToHourMinInt(currentTimeShared.ToLocalTime());
                }
                break;
            }
        }
        RequestSerialization();
    }

    private void AddExitStamp(VRCPlayerApi vp)
    {
        string name = vp.displayName;
        for (int i = 0; i < sync_visitorNames.Length; i++)
        {
            if (sync_visitorNames[i] == name)
            {
                sync_exitTime[i] = DateTimeToHourMinInt(currentTimeShared.ToLocalTime());
                break;
            }
        }
        RequestSerialization();
    }

    // // DisplayNames Alternative func
    // void DisplayNames(Text[] name_text, string[] sync_visitorNames, bool[] is_valid_member, int names_per_object, int last_row_overflow)
    // {
    //     int cur_obj_index = 0;
    //     foreach (Text name_field in name_text)
    //     {
    //         int nmIndex_LoopStart = cur_obj_index * names_per_object;
    //         int nmIndex_LoopEnd = (cur_obj_index + 1) * names_per_object;
    //         if (name_field == name_text.Last()) nmIndex_LoopEnd += last_row_overflow;

    //         StringBuilder row_builder = new StringBuilder();
    //         for (int nmIndex = nmIndex_LoopStart; nmIndex < nmIndex_LoopEnd && nmIndex < sync_visitorNames.Length; nmIndex++)
    //         {
    //             if (string.IsNullOrWhiteSpace(sync_visitorNames[nmIndex])) continue;

    //             row_builder.AppendLine(StringItemOfVisitor(nmIndex, is_valid_member[nmIndex]));
    //         }

    //         name_field.text = row_builder.ToString();
    //         cur_obj_index++;
    //     }
    // }


    private void DisplayElapsedTimeOfInstance()
    {


        if (elapsed_text == null) return;
        if (dontShowElapsedTime)
        {
            elapsed_text.text = "[Disabled]";
            return;
        }

        long current_tick = currentTimeShared.Ticks;
        TimeSpan ts = new TimeSpan(current_tick - sync_createdTick);

        String tx_elapsed = "";
        tx_elapsed += ts.TotalHours < 24 ? "" : ts.Days.ToString("0")
            + SizeFixedString(" DAY(s) + ", fontsize_small_day);
        tx_elapsed += ts.TotalMinutes < 60 ? "" : ts.Hours.ToString("00") + ":";
        tx_elapsed += ts.TotalSeconds < 60 ? "" : ts.Minutes.ToString("00") + ":";
        tx_elapsed += ts.Seconds.ToString("00");

        elapsed_text.text = tx_elapsed;


    }

    private void DisplayVisitor()
    {
        if (!SyncAllValid()) return;

        int visitor_count = 0;
        int inworld_count = VRCPlayerApi.GetPlayerCount();

        string[] st_names_inworld = new string[inworld_count];
        bool[] is_valid_member = new bool[namecount_MAX];


        if (time_text != null && time_text.text == SYNC_WAIT_MESSAGE)
        {
            time_text.text = CurrentDateTimeString(format_date);
        }

        //Valid Name Pick
        players = new VRCPlayerApi[world_capacity * 2];
        VRCPlayerApi.GetPlayers(players);
        foreach (VRCPlayerApi p in players)
        {
            if (p != null)
                AddStringToArr(ref st_names_inworld, p.displayName, false);
        }

        //Name Check
        for (int k = 0; k < sync_visitorNames.Length; k++)
        {
            int findIndex = IndexFindInArr(st_names_inworld, sync_visitorNames[k]);
            is_valid_member[k] = findIndex != -1;
        }

        //Names Display
        int cur_obj_index = 0;
        for (int i = 0; i < name_text.Length; i++)
        {
            if (name_text[i] == null) continue;

            int nmIndex_LoopStart = cur_obj_index * name_per_object;
            int nmIndex_LoopEnd = (cur_obj_index + 1) * name_per_object;
            if (i == name_text.Length - 1) nmIndex_LoopEnd += last_row_overflow;

            string st_this_row = "";
            for (int nmIndex = nmIndex_LoopStart; nmIndex < nmIndex_LoopEnd; nmIndex++)
            {
                if (nmIndex >= sync_visitorNames.Length) continue;
                if (string.IsNullOrWhiteSpace(sync_visitorNames[nmIndex])) continue;

                st_this_row += StringItemOfVisitor(nmIndex, is_valid_member[nmIndex]) + "\n";
                visitor_count++;
            }

            name_text[i].text = st_this_row;
            cur_obj_index++;
        }

        //Number of Visited
        string str_visitor_count = "";

        string str_visitor_total_count = visitor_count.ToString("00");
        if (visitor_count >= namecount_MAX) str_visitor_total_count = "MAX";

        if (stats[current_stat_index] != STAT_ALL_VIEW)
        {
            str_visitor_count = inworld_count.ToString("00");
            str_visitor_count += SizeFixedString(" / " + str_visitor_total_count, fontsize_small_membercount);
        }
        else
        {
            str_visitor_count = str_visitor_total_count;
        }

        visitorsnumber_text.text = str_visitor_count;
    }

    private string StringItemOfVisitor(int index, bool valid)
    {
        String st = "";
        int cur_stat = stats[current_stat_index];

        if (cur_stat == STAT_INWORLD_VIEW)
        {
            st = VisitorNameString(valid, sync_visitorNames[index]);
        }
        else if (cur_stat == STAT_ALL_VIEW)
        {
            st = VisitorNameString(true, sync_visitorNames[index]);
        }
        else if (cur_stat == STAT_SHOW_TIME)
        {
            st = VisitorNameString(valid, sync_visitorNames[index],
            HourMinIntToString(sync_entryTime[index]),
            HourMinIntToString(sync_exitTime[index]));
        }
        //else if (cur_stat == STAT_SHOW_POS)
        //{
        //    st = VisitorNameString(valid, sync_visitorNames[index], sync_position[index], sync_rotation[index]);
        //}
        else if (cur_stat == STAT_BASE64)
        {
            int bc;
            string st_plainb64 = ToBase64(sync_visitorNames[index], out bc);
            st = VisitorNameString(valid, $"{bc:00} {st_plainb64}");
        }
        else
        {
            st = "(undefined)";
        }

        return st;
    }

    //private int AddStringToArr(ref string[] arr,string st) => AddStringToArr(arr,st,true);
    private int AddStringToArr(ref string[] arr, string st, bool allowDupl = true)
    {
        int srcLen = arr.Length;
        if (arr == null) return -1;
        if (srcLen == 0) return -1;
        if (!allowDupl) for (int i = 0; i < arr.Length; i++) if (arr[i] == st) return i;
        //if (!allowDupl) foreach (string s in arr) if (s == st) return -1;

        for (int i = 0; i < srcLen; i++)
        {
            if (string.IsNullOrEmpty(arr[i]))
            {
                arr[i] = st;
                return i;
            }
        }

        //Array.Resize(ref arr,srcLen+1);
        int result = StringArrayResize(ref arr, srcLen + 1);
        if (result != -1) arr[srcLen] = st;

        return result == -1 ? -1 : srcLen;
    }

    private int IndexFindInArr(string[] arr, string st, int findMode = 0)
    {
        if (arr == null) return -1;
        if (arr.Length == 0) return -1;

        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i] == st) return i;
        }
        return -1;
    }

    private string VisitorNameString(bool valid, string name) => ColorFixedString(name, valid ? cl_inworld_code : cl_absent_code);

    private string VisitorNameString(bool valid, string name, string joinTimeSt, string ExitTimeSt)
    {
        string st_time = valid ?
             "[ " + joinTimeSt + " - ]" : "[ " + joinTimeSt + " - " + ExitTimeSt + "]";
        return ColorFixedString(name + SizeFixedString(st_time, fontsize_small_item), valid ? cl_inworld_code : cl_absent_code);
    }
    private string VisitorNameString(bool valid, string name, Vector3 pos, Quaternion rot)
    {
        string st_pos = pos.ToString();
        return ColorFixedString(name + SizeFixedString(" " + st_pos, fontsize_small_item), valid ? cl_inworld_code : cl_absent_code);
    }
    private bool SyncAllValid() =>
    sync_createdTick != 0 &&
    sync_visitorNames != null &&
    sync_entryTime != null &&
    sync_exitTime != null;

    #region DETACHABLE

    private string ColorFixedString(string st, Color clr) => TaggedString("color", StrRGBofColor(clr), st);
    private string ColorFixedString(string st, string colorCode) => TaggedString("color", colorCode, st);
    private int GetFontSizeByScaling(Text origText, float ratio)
    {
        if (origText == null) return 10;
        return (int)(origText.fontSize * ratio) + 1;
    }
    private string SizeFixedString(string st, Text origText, float ratio) => SizeFixedString(st, GetFontSizeByScaling(origText, ratio));
    private string SizeFixedString(string st, int siz) => TaggedString("size", siz.ToString(), st);
    private string TaggedString(string tagName, string param, string content) => "<" + tagName + "=" + param + ">" + content + "</" + tagName + ">";
    private string CurrentDateTimeString(string format) => GetUtcNow().ToLocalTime().ToString(format, CultureInfo.InvariantCulture);
    private int DateTimeToHourMinInt(DateTime dt) => (dt.Hour << 6) + dt.Minute;
    private string HourMinIntToString(int hourMin) => $"{hourMin >> 6}:{hourMin & 0b00111111:00}";
    //private DateTime GetUtcNow() => new DateTime(DateTime.UtcNow.Ticks).ToLocalTime();
    private DateTime GetUtcNow() => new DateTime(DateTime.UtcNow.Ticks);
    private string StrRGBofColor(Color clr) => $"#{(int)(clr.r * 0xff):X2}{(int)(clr.g * 0xff):X2}{(int)(clr.b * 0xff):X2}";
    private static int StringArrayResize(ref string[] ary, int newLen = -1)
    {
        int aryLength = ary != null ? ary.Length : 0;
        if (newLen < 0) newLen = aryLength + 1;
        if (ary == null)
        {
            ary = new string[newLen];
            return newLen;
        }

        int srcLen = ary.Length;
        string[] tmpAry = new string[newLen];

        Array.Copy(ary, tmpAry, Math.Min(ary.Length, tmpAry.Length));
        ary = new string[newLen];
        Array.Copy(tmpAry, ary, newLen);

        return newLen;
    }
    private int VRCApiArrayResize(ref VRCPlayerApi[] ary, int newLen = -1)
    {
        int aryLength = ary != null ? ary.Length : 0;
        if (newLen < 0) newLen = aryLength + 1;
        if (ary == null)
        {
            ary = new VRCPlayerApi[newLen]; return newLen;
        }

        int srcLen = ary.Length;
        VRCPlayerApi[] tmpAry = new VRCPlayerApi[newLen];

        Array.Copy(ary, tmpAry, Math.Min(ary.Length, tmpAry.Length));
        ary = new VRCPlayerApi[newLen];
        Array.Copy(tmpAry, ary, newLen);

        return newLen;
    }

    private int _;
    private string ToBase64(string st) => ToBase64(st, out _);
    private string ToBase64(string st, out int byteCount)
    {
        string inputString = st;
        byte[] utf8Bytes = new byte[inputString.Length * 4];
        byteCount = 0;
        foreach (char c in inputString)
        {
            if (c <= 0x7F) // 1 byte
            {
                utf8Bytes[byteCount++] = (byte)c;
            }
            else if (c <= 0x7FF) // 2 bytes
            {
                utf8Bytes[byteCount++] = (byte)(0xC0 | (c >> 6));
                utf8Bytes[byteCount++] = (byte)(0x80 | (c & 0x3F));
            }
            else if (c <= 0xFFFF) // 3 bytes
            {
                utf8Bytes[byteCount++] = (byte)(0xE0 | (c >> 12));
                utf8Bytes[byteCount++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                utf8Bytes[byteCount++] = (byte)(0x80 | (c & 0x3F));
            }
            else if (c <= 0x10FFFF) // 4 bytes
            {
                utf8Bytes[byteCount++] = (byte)(0xF0 | (c >> 18));
                utf8Bytes[byteCount++] = (byte)(0x80 | ((c >> 12) & 0x3F));
                utf8Bytes[byteCount++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                utf8Bytes[byteCount++] = (byte)(0x80 | (c & 0x3F));
            }
        }
        // Delete Extra Bytes
        byte[] resultBytes = new byte[byteCount];
        Array.Copy(utf8Bytes, resultBytes, byteCount);
        return Convert.ToBase64String(resultBytes);
    }

    #endregion

}
