using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

public static partial class ShortcutActionCatalog
{
    private static IEnumerable<ShortcutActionDefinition> CreateLiveMarkerActions()
    {
        var primary = OperatingSystem.IsMacOS() ? "Meta" : "Ctrl";
        return
        [
            new(ShortcutActionKeys.LIVE_MARKER_NEW, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.New", $"{primary}+N"),
            new(ShortcutActionKeys.LIVE_MARKER_OPEN, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Open", $"{primary}+O"),
            new(ShortcutActionKeys.LIVE_MARKER_SAVE, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Save", $"{primary}+S"),
            new(ShortcutActionKeys.LIVE_MARKER_SAVE_AS, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.SaveAs", $"{primary}+Alt+S"),
            new(ShortcutActionKeys.LIVE_MARKER_CLOSE, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Close", $"{primary}+Alt+W"),
            new(ShortcutActionKeys.LIVE_MARKER_UNDO, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Undo", $"{primary}+Z"),
            new(ShortcutActionKeys.LIVE_MARKER_REDO, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Redo", OperatingSystem.IsMacOS() ? "Meta+Shift+Z" : "Ctrl+Y"),
            new(ShortcutActionKeys.LIVE_MARKER_EXPORT_TEXT, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.ExportText", $"{primary}+Shift+T"),
            new(ShortcutActionKeys.LIVE_MARKER_EXPORT_EXCEL, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.ExportExcel", $"{primary}+Shift+E"),
            new(ShortcutActionKeys.LIVE_MARKER_LOCK_TIME, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.LockTime", "Escape"),
            new(ShortcutActionKeys.LIVE_MARKER_UNLOCK_TIME, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.UnlockTime", "Shift+Escape"),
            new(ShortcutActionKeys.LIVE_MARKER_DELETE, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Delete", "Delete"),
            new(ShortcutActionKeys.LIVE_MARKER_ADD, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Add", "Enter"),
            new(ShortcutActionKeys.LIVE_MARKER_COLOR_1, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Color1", $"{primary}+1"),
            new(ShortcutActionKeys.LIVE_MARKER_COLOR_2, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Color2", $"{primary}+2"),
            new(ShortcutActionKeys.LIVE_MARKER_COLOR_3, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Color3", $"{primary}+3"),
            new(ShortcutActionKeys.LIVE_MARKER_COLOR_4, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Color4", $"{primary}+4"),
            new(ShortcutActionKeys.LIVE_MARKER_COLOR_5, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Color5", $"{primary}+5"),
            new(ShortcutActionKeys.LIVE_MARKER_COLOR_6, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Color6", $"{primary}+6"),
            new(ShortcutActionKeys.LIVE_MARKER_COLOR_7, "liveMarker", "App.Shortcut.Group.liveMarker", "LiveMarker.Shortcut.Color7", $"{primary}+7")
        ];
    }
}
