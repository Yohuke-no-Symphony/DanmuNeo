using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 快捷键动作目录。
/// </summary>
public static class ShortcutActionCatalog
{
    /// <summary>
    /// 全部快捷键动作。
    /// </summary>
    public static IReadOnlyList<ShortcutActionDefinition> Actions { get; } =
    [
        new(ShortcutActionKeys.LIVE_ROOM_START_LISTENING, "liveRoom", "直播间", "监听开启", "Ctrl+Alt+P"),
        new(ShortcutActionKeys.LIVE_ROOM_STOP_LISTENING, "liveRoom", "直播间", "监听关闭", "Ctrl+Alt+O"),
        new(ShortcutActionKeys.LIVE_PLAYER_START, "livePlayer", "追帧", "开始播放", "Ctrl+Shift+P"),
        new(ShortcutActionKeys.LIVE_PLAYER_STOP, "livePlayer", "追帧", "停止播放", "Ctrl+Shift+O"),
        new(ShortcutActionKeys.LIVE_PLAYER_CHASE, "livePlayer", "追帧", "追帧刷新", "Ctrl+Shift+R"),
        new(ShortcutActionKeys.INPUT_FOCUS_DRAFT, "input", "输入", "聚焦同传框", "Ctrl+Shift+I"),
        new(ShortcutActionKeys.INPUT_CLEAR_DRAFT, "input", "输入", "清除同传框", "Ctrl+Shift+Backspace"),
        new(ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP, "input", "输入", "轮切同传样式预设", "Tab"),
        new(ShortcutActionKeys.LYRIC_START_SENDING, "lyric", "歌词", "开始发送", "Ctrl+Shift+A"),
        new(ShortcutActionKeys.LYRIC_STOP_SENDING, "lyric", "歌词", "停止发送", "Ctrl+Shift+S"),
        new(ShortcutActionKeys.LYRIC_SEEK_BACKWARD, "lyric", "歌词", "微调快退", "Ctrl+Shift+Left"),
        new(ShortcutActionKeys.LYRIC_SEEK_FORWARD, "lyric", "歌词", "微调快进", "Ctrl+Shift+Right")
    ];
}
