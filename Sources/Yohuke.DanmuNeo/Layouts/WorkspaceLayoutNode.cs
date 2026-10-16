namespace Yohuke.DanmuNeo.Layouts;

internal sealed record WorkspaceLayoutNode
{
    public WorkspaceLayoutNodeKind Kind { get; init; } = WorkspaceLayoutNodeKind.Tabs;
    public WorkspaceLayoutOrientation Orientation { get; init; } = WorkspaceLayoutOrientation.Horizontal;
    public double Proportion { get; init; } = 1;
    public List<WorkspaceLayoutNode> Children { get; init; } = [];
    public List<string> PanelIds { get; init; } = [];
    public string? ActivePanelId { get; init; }
}
