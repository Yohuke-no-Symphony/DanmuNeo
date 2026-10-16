namespace Yohuke.DanmuNeo.Layouts;

internal sealed record WorkspaceLayoutSnapshot
{
    public WorkspaceLayoutNode? Main { get; init; }
    public List<WorkspaceFloatingLayout> FloatingWindows { get; init; } = [];
    public List<string> HiddenPanelIds { get; init; } = [];
    public string? FocusedPanelId { get; init; }
}
