using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UdonSharp;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class VisitorsBoardManager : UdonSharpBehaviour
{
#region FIELDS

    //INSPECTOR
    [Header("訪問者の名前の色")]
    [SerializeField] private Color cl_inworld;
    private string colorCodeAttendingMember = "#FFFFFF";

    [Header("訪問者の名前の色 (退室後)")]
    [SerializeField] private Color cl_absent;
    private string colorCodeLeftMember = "#666666";

    [Header("入退室時刻を表示への切り替えを有効にする")]
    [SerializeField] private bool enableEntryExitTimeView = false;

    [Header("BASE64確認表示の切り替えを有効にする")]
    [SerializeField] private bool enableBase64View = false;

    [Header("インスタンス経過時間を表示しない")]
    [SerializeField] private bool dontShowElapsedTime = false;

    [Header("最新の入室時間を入室時間として使う")]
    [SerializeField] private bool useNewestJoinTime = true;

    [Header("１列に入る人数")]
    [SerializeField, Range(1, 100)] private int namePerObject = 20;

    [Header("最終列溢れ人数")]
    [SerializeField, Range(0, 1000)] private int lastRowAdditionalLine = 200;

    [Header("Object Slots")]
    [SerializeField] private Text time_text = null;
    [SerializeField] private Text elapsed_text = null;
    [SerializeField] private Text visitorsnumber_text = null;
    [SerializeField] private Text[] name_text = null;

    [Header("Date Format - Header")]
    [SerializeField] private string formatDate = "yyyy-MM-dd  HH:mm:ss";

    [Header("Time Format - Member")]
    [SerializeField] private string formatTime = "HH:mm";

    //SYNCED
    [UdonSynced] private long sync_createdTick = 0;
    [UdonSynced] private string[] sync_visitorNames = null;
    [UdonSynced] private int[] sync_timeStampPack = null;

    //INTERNAL
    private int currentStatIndex = 0;
    private int[] statsViewMode = null;
    private int smallFontSizeDay = 40;
    private int smallFontSizeMemberCount = 30;
    private int smallFontSizeItem = 10;
    private int nameCountMAX = 1;
    VRCPlayerApi[] players = null;
    VRCPlayerApi localPlayer = null;
    private int syncErrCount = 0;
    private int resetSyncErrCount = 60;
    private int nameErrCount = 0;
    private int restoreNameErrCount = 5;
    private DateTime currentTimeShared = new DateTime();

    //INTERNAL MINOR
    private int cur_sec = 0;
    private int prev_sec = 0;
    private int late_update_counter = 2;

    //CONSTANTS
    private const int TIME_DELAY_UPDATE = 2;
    private const int WORLD_MAX_CAPACITY = 64;
    private const int STAT_INWORLD_VIEW = 90;
    private const int STAT_ALL_VIEW = 91;
    private const int STAT_SHOW_TIME = 92;
    private const int STAT_SHOW_POS = 93;
    private const int STAT_BASE64 = 94;
    private const int STAT_ACHIEVE = 95;
    private const int STAT_OPTIONS = 96;
    private const string SYNC_WAIT_MESSAGE = "Waiting for sync...";

#endregion

    //UNITY EVENTS
    private void Start()
    {
        VRCPlayerApi[] players = new VRCPlayerApi[WORLD_MAX_CAPACITY * 2];
        AllocStats();
        currentStatIndex = 0;

        localPlayer = Networking.LocalPlayer;

        nameCountMAX = (name_text.Length * namePerObject > 0) ? name_text.Length * namePerObject + lastRowAdditionalLine : 1;

        if (elapsed_text != null) smallFontSizeDay = GetFontSizeByScaling(elapsed_text, 0.4f);
        if (visitorsnumber_text != null) smallFontSizeMemberCount = GetFontSizeByScaling(visitorsnumber_text, 0.6f);
        if (name_text != null && name_text[0] != null) smallFontSizeItem = GetFontSizeByScaling(name_text[0], 0.5f);

        colorCodeAttendingMember = StrRGBofColor(cl_inworld);
        colorCodeLeftMember = StrRGBofColor(cl_absent);

        for (int i = 0; i < name_text.Length; i++)
        {
            if (name_text[i] == null) continue;
            if (namePerObject > 1)
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
        bool sync_valid = SyncAllValid();
        currentTimeShared = GetUtcNow();

        cur_sec = currentTimeShared.Minute * 60 + currentTimeShared.Second;

        if (prev_sec != cur_sec)
        {
            prev_sec = cur_sec;
            if (sync_valid) DisplayElapsedTimeOfInstance();

            //detect errors - whole
            // if (sync_createdTick == 0) syncErrCount++;
            if (!sync_valid) syncErrCount++;
            if (syncErrCount > resetSyncErrCount)
            {
                Networking.SetOwner(localPlayer, this.gameObject);
                InitializeInformation(true, true);
                syncErrCount = 0;
                nameErrCount = 0;
            }

            //detect errors - names
            if (sync_visitorNames == null || sync_visitorNames.Length == 0) nameErrCount++;
            if(nameErrCount > restoreNameErrCount)
            {
                Networking.SetOwner(localPlayer, this.gameObject);
                ResetNameByAttending(true);
                nameErrCount = 0;
            }

            //late update
            if (late_update_counter > 0)
            {
                if (late_update_counter == 1 && sync_valid) DisplayVisitor();
                late_update_counter--;
            }
            
        }
    }

    public override void OnDeserialization()
    {
        if (!SyncAllValid()) return;
        DisplayVisitor();
        late_update_counter = 0;
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (!SyncAllValid()) return;
        late_update_counter = TIME_DELAY_UPDATE;
        if (Networking.IsOwner(this.gameObject))
        {
            AddNameAndJoinStamp(player, out _);
            DisplayVisitor();
        }
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (!SyncAllValid()) return;
        late_update_counter = TIME_DELAY_UPDATE;
        if (Networking.IsOwner(this.gameObject))
        {
            AddExitStamp(player, out _);
            DisplayVisitor();
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

        statsViewMode = new int[statcount];

        statsViewMode[0] = STAT_INWORLD_VIEW;
        statsViewMode[1] = STAT_ALL_VIEW;

        if (enableEntryExitTimeView) statsViewMode[index++] = STAT_SHOW_TIME;
        //if(enablePositionView) statsViewMode[index] = STAT_SHOW_POS;
        if (enableBase64View) statsViewMode[index++] = STAT_BASE64;
    }

    private void InitializeInformation(bool owner, bool restore = false)
    {
        if (owner)
        {
            Networking.SetOwner(Networking.LocalPlayer, this.gameObject);

            sync_createdTick = DateTime.UtcNow.Ticks;
            sync_visitorNames = new string[nameCountMAX];
            sync_timeStampPack = new int[nameCountMAX];

            for (int i = 0; i < sync_visitorNames.Length; i++)
            {
                sync_visitorNames[i] = string.Empty;
                sync_timeStampPack[i] = 0;
            }

            if (restore) ResetNameByAttending(false);
            
            RequestSerialization();
        }
        else
        {
            sync_createdTick = DateTime.UtcNow.Ticks;
            sync_visitorNames = new string[nameCountMAX];
            sync_timeStampPack = new int[nameCountMAX];
        }
    }

    private void ResetNameByAttending(bool req_serialize = true)
    {
        players = new VRCPlayerApi[WORLD_MAX_CAPACITY * 2];
        VRCPlayerApi.GetPlayers(players);
        foreach (VRCPlayerApi vp in players)
        {
            AddNameAndJoinStamp(vp, out _, false);
        }

        if (req_serialize) RequestSerialization();
    }

    private void SwitchStatToNext() => currentStatIndex = (currentStatIndex + 1) % statsViewMode.Length;

    private void AddNameAndJoinStamp(VRCPlayerApi vp, out int index, bool req_serialize = true)
    {
        index = -1;
        if (vp == null) return;
        
        string name = vp.displayName;
  
        for (int i = 0; i < sync_visitorNames.Length; i++)
        {
            if (string.IsNullOrEmpty(sync_visitorNames[i]))
            {
                sync_visitorNames[i] = name;
                sync_timeStampPack[i] = DateTimeToHourMinInt(currentTimeShared.ToLocalTime()) << 16;
                index = i;
                break;
            }
            else if (sync_visitorNames[i] == name)
            {
                if (useNewestJoinTime)
                {
                    sync_timeStampPack[i] = DateTimeToHourMinInt(currentTimeShared.ToLocalTime()) << 16;
                }
                index = i;
                break;
            }
        }
        if (req_serialize) RequestSerialization();
    }

    private void AddExitStamp(VRCPlayerApi vp, out int index)
    {
        index = -1;
        if (vp == null) return;

        string name = vp.displayName;
  
        for (int i = 0; i < sync_visitorNames.Length; i++)
        {
            if (sync_visitorNames[i] == name)
            {
                sync_timeStampPack[i] &= 0xFFFF0000;
                sync_timeStampPack[i] |= DateTimeToHourMinInt(currentTimeShared.ToLocalTime());
                index = i;
                break;
            }
        }
        RequestSerialization();
    }

    private void DisplayElapsedTimeOfInstance()
    {
        if (elapsed_text == null) return;
        if (dontShowElapsedTime)
        {
            elapsed_text.text = "[Disabled]";
            return;
        }

        TimeSpan ts = new TimeSpan(currentTimeShared.Ticks - sync_createdTick);

        String tx_elapsed = "";
        tx_elapsed += ts.TotalHours < 24 ? "" : ts.Days.ToString("0")
            + SizeFixedString(" DAY(s) + ", smallFontSizeDay);
        tx_elapsed += ts.TotalMinutes < 60 ? "" : ts.Hours.ToString("00") + ":";
        tx_elapsed += ts.TotalSeconds < 60 ? "" : ts.Minutes.ToString("00") + ":";
        tx_elapsed += ts.Seconds.ToString("00");

        elapsed_text.text = tx_elapsed;
    }

    private void DisplayVisitor()
    {
        if (!SyncAllValid()) return;

        int visitor_count = 0;
        int attending_count = VRCPlayerApi.GetPlayerCount();

        string[] st_names_attending = new string[attending_count];
        bool[] is_valid_member = new bool[nameCountMAX];

        //Created Time
        if (time_text != null)
        {
            time_text.text = CreatedDateTimeString(formatDate);
        }

        //Valid Name Pick
        players = new VRCPlayerApi[WORLD_MAX_CAPACITY * 2];
        VRCPlayerApi.GetPlayers(players);
        foreach (VRCPlayerApi vp in players)
        {
            if (vp != null) AddStringToArr(ref st_names_attending, vp.displayName, false);
        }

        //Name Check
        for (int k = 0; k < sync_visitorNames.Length; k++)
        {
            int findIndex = IndexFindInArr(st_names_attending, sync_visitorNames[k]);
            is_valid_member[k] = findIndex != -1;
        }

        //Names Display
        int cur_obj_index = 0;
        for (int i = 0; i < name_text.Length; i++)
        {
            if (name_text[i] == null) continue;

            int nmIndex_LoopStart = cur_obj_index * namePerObject;
            int nmIndex_LoopEnd = (cur_obj_index + 1) * namePerObject;
            if (i == name_text.Length - 1) nmIndex_LoopEnd += lastRowAdditionalLine;

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
        if (visitor_count >= nameCountMAX) str_visitor_total_count = "MAX";

        if (statsViewMode[currentStatIndex] != STAT_ALL_VIEW)
        {
            str_visitor_count = attending_count.ToString("00");
            str_visitor_count += SizeFixedString(" / " + str_visitor_total_count, smallFontSizeMemberCount);
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
        int cur_stat = statsViewMode[currentStatIndex];

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
            HourMinIntToString(sync_timeStampPack[index] >> 16),
            HourMinIntToString(sync_timeStampPack[index] & 0x0000FFFF);
        }
        //else if (cur_stat == STAT_SHOW_POS)
        //{
        //    st = VisitorNameString(valid, sync_visitorNames[index], sync_position[index], sync_rotation[index]);
        //}
        else if (cur_stat == STAT_BASE64)
        {
            int bc;
            string st_plainb64 = ToBase64(sync_visitorNames[index], out bc);
            st = VisitorNameString(valid, $"{bc:00} {st_plainb64} {sync_timeStampPack[index]}");
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

    private string VisitorNameString(bool valid, string name) => ColorFixedString(name, valid ? colorCodeAttendingMember : colorCodeLeftMember);
    private string VisitorNameString(bool valid, string name, string joinTimeSt, string ExitTimeSt)
    {
        string st_time = valid ? $"[{joinTimeSt} - ]" : $"[{joinTimeSt} - {ExitTimeSt}]";
        return ColorFixedString(name + SizeFixedString(st_time, smallFontSizeItem), valid ? colorCodeAttendingMember : colorCodeLeftMember);
    }
    private string VisitorNameString(bool valid, string name, Vector3 pos, Quaternion rot)
    {
        string st_pos = pos.ToString();
        return ColorFixedString(name + SizeFixedString(" " + st_pos, smallFontSizeItem), valid ? colorCodeAttendingMember : colorCodeLeftMember);
    }
    private bool SyncAllValid() =>
    sync_createdTick != 0 &&
    sync_visitorNames != null &&
    sync_timeStampPack != null;


    private DateTime GetCreatedDT() => new DateTime(sync_createdTick);
    //private string CreatedDateTimeString(string format) => GetCreatedDT().ToLocalTime().ToString(format, CultureInfo.InvariantCulture);
    private string CreatedDateTimeString(string format)
        => sync_createdTick > 0 ? DateTimeStringByGlobalTick(sync_createdTick, format) : "invalid date";
    private string CreatedDateTimeString() => CreatedDateTimeString(formatDate);

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

    private DateTime GetUtcNow() => new DateTime(DateTime.UtcNow.Ticks);
    private string CurrentDateTimeString(string format) => GetUtcNow().ToLocalTime().ToString(format, CultureInfo.InvariantCulture);
    private string DateTimeStringByGlobalTick(long glb_tick, string format)
        => new DateTime(glb_tick).ToLocalTime().ToString(format, CultureInfo.InvariantCulture);

    private int DateTimeToHourMinInt(DateTime dt) => (dt.Hour << 6) + dt.Minute;
    private string HourMinIntToString(int hourMin) => $"{hourMin >> 6}:{hourMin & 0b00111111:00}";

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
    
    //人人人人人人人_
    private int _;//＜
    //Y^Y^Y^Y^Y^Y^Y￣

}
