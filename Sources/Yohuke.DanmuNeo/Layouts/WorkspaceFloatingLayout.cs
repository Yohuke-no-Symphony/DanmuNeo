namespace Yohuke.DanmuNeo.Layouts;

internal sealed record WorkspaceFloatingLayout
{
    public WorkspaceLayoutNode Layout { get; init; } = new();
    public WorkspaceWindowBounds Bounds { get; init; } = new();
}
