namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 快捷键动作键。
/// </summary>
public static class ShortcutActionKeys
{
    /// <summary>
    /// 开始监听直播间。
    /// </summary>
    public const string LIVE_ROOM_START_LISTENING = "liveRoom.startListening";

    /// <summary>
    /// 停止监听直播间。
    /// </summary>
    public const string LIVE_ROOM_STOP_LISTENING = "liveRoom.stopListening";

    /// <summary>
    /// 开始播放直播。
    /// </summary>
    public const string LIVE_PLAYER_START = "livePlayer.start";

    /// <summary>
    /// 停止播放直播。
    /// </summary>
    public const string LIVE_PLAYER_STOP = "livePlayer.stop";

    /// <summary>
    /// 追到最新直播流。
    /// </summary>
    public const string LIVE_PLAYER_CHASE = "livePlayer.chase";

    /// <summary>
    /// 聚焦同传输入框。
    /// </summary>
    public const string INPUT_FOCUS_DRAFT = "input.focusDraft";

    /// <summary>
    /// 清空同传输入框。
    /// </summary>
    public const string INPUT_CLEAR_DRAFT = "input.clearDraft";

    /// <summary>
    /// 轮切当前工作区的同传样式预设。
    /// </summary>
    public const string INPUT_CYCLE_MARK_GROUP = "input.cycleMarkGroup";

    /// <summary>
    /// 开始发送歌词。
    /// </summary>
    public const string LYRIC_START_SENDING = "lyric.startSending";

    /// <summary>
    /// 停止发送歌词。
    /// </summary>
    public const string LYRIC_STOP_SENDING = "lyric.stopSending";

    /// <summary>
    /// 歌词快退。
    /// </summary>
    public const string LYRIC_SEEK_BACKWARD = "lyric.seekBackward";

    /// <summary>
    /// 歌词快进。
    /// </summary>
    public const string LYRIC_SEEK_FORWARD = "lyric.seekForward";

    /// <summary>
    /// 跳到下一句歌词并发送。
    /// </summary>
    public const string LYRIC_SEEK_NEXT = "lyric.seekNext";
}
