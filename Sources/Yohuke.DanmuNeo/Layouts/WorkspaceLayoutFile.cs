namespace Yohuke.DanmuNeo.Layouts;

internal sealed record WorkspaceLayoutFile
{
    internal const int CURRENT_VERSION = 1;

    public int Version { get; init; } = CURRENT_VERSION;
    public string CurrentPresetId { get; init; } = WorkspaceLayoutPresets.DEFAULT;
    public WorkspaceLayoutSnapshot CurrentLayout { get; init; } = new();
    public List<WorkspaceLayoutPreset> UserPresets { get; init; } = [];
}
