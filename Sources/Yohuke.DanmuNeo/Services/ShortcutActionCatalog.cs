using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 快捷键动作目录。
/// </summary>
public static partial class ShortcutActionCatalog
{
    /// <summary>
    /// 全部快捷键动作。
    /// </summary>
    public static IReadOnlyList<ShortcutActionDefinition> Actions { get; } =
    [
        new(ShortcutActionKeys.LIVE_ROOM_START_LISTENING, "liveRoom", "App.Shortcut.Group.liveRoom", "App.Shortcut.Action.LiveRoomStartListening", "Ctrl+Alt+P"),
        new(ShortcutActionKeys.LIVE_ROOM_STOP_LISTENING, "liveRoom", "App.Shortcut.Group.liveRoom", "App.Shortcut.Action.LiveRoomStopListening", "Ctrl+Alt+O"),
        new(ShortcutActionKeys.DANMU_SCROLL_TO_TOP, "danmu", "App.Shortcut.Group.danmu", "App.Shortcut.Action.DanmuScrollToTop", "Alt+Home"),
        new(ShortcutActionKeys.LIVE_PLAYER_START, "livePlayer", "App.Shortcut.Group.livePlayer", "App.Shortcut.Action.LivePlayerStart", "Ctrl+Shift+P"),
        new(ShortcutActionKeys.LIVE_PLAYER_STOP, "livePlayer", "App.Shortcut.Group.livePlayer", "App.Shortcut.Action.LivePlayerStop", "Ctrl+Shift+O"),
        new(ShortcutActionKeys.LIVE_PLAYER_CHASE, "livePlayer", "App.Shortcut.Group.livePlayer", "App.Shortcut.Action.LivePlayerChase", "Ctrl+Shift+R"),
        new(ShortcutActionKeys.LIVE_PLAYER_CAPTURE, "livePlayer", "App.Shortcut.Group.livePlayer", "App.Shortcut.Action.LivePlayerCapture", "Alt+P"),
        new(ShortcutActionKeys.INPUT_FOCUS_DRAFT, "input", "App.Shortcut.Group.input", "App.Shortcut.Action.InputFocusDraft", "Ctrl+Shift+I"),
        new(ShortcutActionKeys.INPUT_CLEAR_DRAFT, "input", "App.Shortcut.Group.input", "App.Shortcut.Action.InputClearDraft", "Ctrl+Shift+Backspace"),
        new(ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP, "input", "App.Shortcut.Group.input", "App.Shortcut.Action.InputCycleMarkGroup", "Tab"),
        new(ShortcutActionKeys.LYRIC_START_SENDING, "lyric", "App.Shortcut.Group.lyric", "App.Shortcut.Action.LyricStartSending", "Ctrl+Shift+A"),
        new(ShortcutActionKeys.LYRIC_STOP_SENDING, "lyric", "App.Shortcut.Group.lyric", "App.Shortcut.Action.LyricStopSending", "Ctrl+Shift+S"),
        new(ShortcutActionKeys.LYRIC_COPY_SELECTED, "lyric", "App.Shortcut.Group.lyric", "App.Shortcut.Action.LyricCopySelected",
            OperatingSystem.IsMacOS() ? "Meta+C" : "Ctrl+C"),
        new(ShortcutActionKeys.LYRIC_SEEK_BACKWARD, "lyric", "App.Shortcut.Group.lyric", "App.Shortcut.Action.LyricSeekBackward", "Ctrl+Shift+Left"),
        new(ShortcutActionKeys.LYRIC_SEEK_FORWARD, "lyric", "App.Shortcut.Group.lyric", "App.Shortcut.Action.LyricSeekForward", "Ctrl+Shift+Right"),
        new(ShortcutActionKeys.LYRIC_SEEK_NEXT, "lyric", "App.Shortcut.Group.lyric", "App.Shortcut.Action.LyricSeekNext", "Ctrl+Shift+Down"),
        .. CreateLiveMarkerActions()
    ];
}
