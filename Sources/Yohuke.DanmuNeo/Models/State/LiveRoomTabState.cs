namespace Yohuke.DanmuNeo.Models.State;

/// <summary>
/// 直播间标签页状态。
/// </summary>
public class LiveRoomTabState
{
    /// <summary>该房间标签上次打开的直播记录文件。</summary>
    public string LiveMarkerFilePath { get; set; } = "";

    /// <summary>
    /// 标签页 ID。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 直播间 ID。
    /// </summary>
    public string RoomId { get; set; } = "";

    /// <summary>
    /// 直播间显示名称。
    /// </summary>
    public string RoomName { get; set; } = "未命名直播间";

    /// <summary>
    /// 主播 UID。
    /// </summary>
    public string OwnerUid { get; set; } = "";

    /// <summary>
    /// 排序序号。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 标签页账号覆盖 ID。
    /// </summary>
    public string? AccountOverrideId { get; set; }

    /// <summary>
    /// 主播头像缓存路径。
    /// </summary>
    public string AvatarPath { get; set; } = "";

    /// <summary>
    /// 同传输入草稿。
    /// </summary>
    public string InputDraft { get; set; } = "";

    /// <summary>
    /// 当前歌词标题。
    /// </summary>
    public string LyricTitle { get; set; } = "";

    /// <summary>
    /// 当前歌词文本。
    /// </summary>
    public string LyricText { get; set; } = "";

    /// <summary>
    /// 当前翻译歌词文本。
    /// </summary>
    public string TranslatedLyricText { get; set; } = "";

    /// <summary>
    /// 歌词自动播放速率。
    /// </summary>
    public double LyricPlaybackRate { get; set; } = 1.0;

    /// <summary>
    /// 歌词发送内容模式。
    /// </summary>
    public LyricSendMode LyricSendMode { get; set; } = LyricSendMode.Bilingual;

    /// <summary>
    /// 歌词退回重播时是否避免重复发送。
    /// </summary>
    public bool PreventRepeatedLyricSend { get; set; } = true;

    /// <summary>
    /// 直播播放器音量百分比。
    /// </summary>
    public double LivePlayerVolumePercent { get; set; } = 100;

    /// <summary>
    /// 直播播放器是否静音。
    /// </summary>
    public bool IsLivePlayerMuted { get; set; }

    /// <summary>
    /// 弹幕转发规则。
    /// </summary>
    public List<DanmuForwardRuleState> ForwardRules { get; set; } = [];
}
