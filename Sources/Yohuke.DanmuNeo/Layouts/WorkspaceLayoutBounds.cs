namespace Yohuke.DanmuNeo.Layouts;

internal static class WorkspaceLayoutBounds
{
    internal static WorkspaceWindowBounds Clamp(WorkspaceWindowBounds bounds, IReadOnlyList<WorkspaceScreenArea> screens)
    {
        var usableScreens = screens.Where(screen =>
            double.IsFinite(screen.X) && double.IsFinite(screen.Y) &&
            double.IsFinite(screen.Width) && screen.Width > 0 &&
            double.IsFinite(screen.Height) && screen.Height > 0 &&
            double.IsFinite(screen.Scaling) && screen.Scaling > 0).ToArray();

        if (usableScreens.Length == 0)
        {
            return bounds;
        }

        var selected = usableScreens.OrderByDescending(screen => IntersectionArea(bounds, screen))
            .ThenBy(screen => Math.Abs(bounds.X - screen.X) + Math.Abs(bounds.Y - screen.Y))
            .First();
        var width = Math.Min(Math.Max(bounds.Width, 260), selected.Width / selected.Scaling);
        var height = Math.Min(Math.Max(bounds.Height, 180), selected.Height / selected.Scaling);
        var maximumX = Math.Max(selected.X, selected.X + selected.Width - width * selected.Scaling);
        var maximumY = Math.Max(selected.Y, selected.Y + selected.Height - height * selected.Scaling);

        return bounds with
        {
            X = Math.Clamp(bounds.X, selected.X, maximumX),
            Y = Math.Clamp(bounds.Y, selected.Y, maximumY),
            Width = width,
            Height = height,
            Scaling = selected.Scaling
        };
    }

    private static double IntersectionArea(WorkspaceWindowBounds bounds, WorkspaceScreenArea screen)
    {
        var width = Math.Max(0, Math.Min(bounds.X + bounds.Width * bounds.Scaling, screen.X + screen.Width) - Math.Max(bounds.X, screen.X));
        var height = Math.Max(0, Math.Min(bounds.Y + bounds.Height * bounds.Scaling, screen.Y + screen.Height) - Math.Max(bounds.Y, screen.Y));
        return width * height;
    }
}
