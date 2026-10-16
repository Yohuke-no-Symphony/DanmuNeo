namespace Yohuke.DanmuNeo.Layouts;

internal sealed record WorkspaceWindowBounds
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 720;
    public double Height { get; init; } = 480;
    public double Scaling { get; init; } = 1;
}
