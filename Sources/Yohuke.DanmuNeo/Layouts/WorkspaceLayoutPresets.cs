using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Layouts;

internal static class WorkspaceLayoutPresets
{
    internal const string DEFAULT = "default";
    internal const string TRANSLATION = "translation";
    internal const string LYRICS = "lyrics";
    internal const string FORWARDING = "forwarding";

    internal static IReadOnlyList<WorkspaceLayoutPreset> BuiltIn => Array.AsReadOnly(new[]
    {
        CreatePreset(DEFAULT, "默认"),
        CreatePreset(TRANSLATION, "同传"),
        CreatePreset(LYRICS, "歌词"),
        CreatePreset(FORWARDING, "转发")
    });

    internal static WorkspaceLayoutFile CreateDefaultFile()
    {
        return new()
        {
            CurrentPresetId = DEFAULT,
            CurrentLayout = Create(DEFAULT)
        };
    }

    internal static string GetDisplayName(string id, string customName)
    {
        var key = id switch
        {
            DEFAULT => "Layout.Preset.Default",
            TRANSLATION => "Layout.Preset.Translation",
            LYRICS => "Layout.Preset.Lyrics",
            FORWARDING => "Layout.Preset.Forwarding",
            _ => null
        };
        return key is null ? customName : Localization.Get(key);
    }

    internal static WorkspaceLayoutSnapshot Create(string presetId)
    {
        var mainPanelId = presetId switch
        {
            DEFAULT or TRANSLATION => WorkbenchPanelIds.LIVE_PLAYER,
            LYRICS => WorkbenchPanelIds.LYRICS,
            FORWARDING => WorkbenchPanelIds.FORWARDING,
            _ => throw new ArgumentException(Localization.Get("Layout.UnknownBuiltIn"), nameof(presetId))
        };
        var right = presetId switch
        {
            DEFAULT => Split(WorkspaceLayoutOrientation.Vertical, 0.3,
                Tabs(0.3, WorkbenchPanelIds.SUPER_CHAT),
                Tabs(0.7, WorkbenchPanelIds.DANMU, WorkbenchPanelIds.HISTORY,
                    WorkbenchPanelIds.LYRICS, WorkbenchPanelIds.FORWARDING, WorkbenchPanelIds.LOG)),
            TRANSLATION => Split(WorkspaceLayoutOrientation.Vertical, 0.3,
                Tabs(0.25, WorkbenchPanelIds.SUPER_CHAT),
                Tabs(0.5, WorkbenchPanelIds.DANMU),
                Tabs(0.25, WorkbenchPanelIds.HISTORY, WorkbenchPanelIds.LOG)),
            _ => Split(WorkspaceLayoutOrientation.Vertical, 0.3,
                Tabs(0.45, WorkbenchPanelIds.LIVE_PLAYER),
                Tabs(0.55, WorkbenchPanelIds.DANMU, WorkbenchPanelIds.SUPER_CHAT, WorkbenchPanelIds.HISTORY,
                    presetId == LYRICS ? WorkbenchPanelIds.FORWARDING : WorkbenchPanelIds.LYRICS, WorkbenchPanelIds.LOG))
        };

        return new()
        {
            Main = Split(WorkspaceLayoutOrientation.Horizontal, 1,
                Tabs(0.2, WorkbenchPanelIds.WORKSPACE),
                Split(WorkspaceLayoutOrientation.Vertical, 0.5,
                    Tabs(0.75, mainPanelId),
                    Tabs(0.25, WorkbenchPanelIds.COMPOSER)),
                right),
            HiddenPanelIds = presetId == TRANSLATION
                ? [WorkbenchPanelIds.LYRICS, WorkbenchPanelIds.FORWARDING]
                : [],
            FocusedPanelId = WorkbenchPanelIds.COMPOSER
        };
    }

    private static WorkspaceLayoutPreset CreatePreset(string id, string name)
    {
        return new() { Id = id, Name = name, Layout = Create(id) };
    }

    private static WorkspaceLayoutNode Tabs(double proportion, params string[] panelIds)
    {
        return new()
        {
            Kind = WorkspaceLayoutNodeKind.Tabs,
            Proportion = proportion,
            PanelIds = [.. panelIds],
            ActivePanelId = panelIds[0]
        };
    }

    private static WorkspaceLayoutNode Split(
        WorkspaceLayoutOrientation orientation,
        double proportion,
        params WorkspaceLayoutNode[] children)
    {
        return new()
        {
            Kind = WorkspaceLayoutNodeKind.Split,
            Orientation = orientation,
            Proportion = proportion,
            Children = [.. children]
        };
    }
}
