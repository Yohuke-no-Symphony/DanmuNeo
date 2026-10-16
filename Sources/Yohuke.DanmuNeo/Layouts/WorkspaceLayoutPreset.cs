namespace Yohuke.DanmuNeo.Layouts;

internal sealed record WorkspaceLayoutPreset
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public WorkspaceLayoutSnapshot Layout { get; init; } = new();
}
