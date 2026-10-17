using Avalonia;

namespace Yohuke.DanmuNeo.Windowing;

internal static class MacOsCaptionGeometry
{
    internal static Rect GetClientBounds(MacOsRect buttonInContent, MacOsRect contentBounds, bool contentIsFlipped)
    {
        if (!IsUsable(buttonInContent) || !IsUsable(contentBounds))
        {
            return default;
        }

        var x = buttonInContent.X - contentBounds.X;
        var y = contentIsFlipped
            ? buttonInContent.Y - contentBounds.Y
            : contentBounds.Y + contentBounds.Height - buttonInContent.Y - buttonInContent.Height;
        return double.IsFinite(x) && double.IsFinite(y)
            ? new(x, y, buttonInContent.Width, buttonInContent.Height)
            : default;
    }

    internal static double GetTitleBarHeight(double minimumHeight, Rect bounds)
    {
        if (!double.IsFinite(minimumHeight) || minimumHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumHeight));
        }

        var height = bounds.Center.Y * 2;
        return double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) && bounds.Width > 0 &&
               bounds.Height > 0 && double.IsFinite(height) && height > 0
            ? Math.Max(minimumHeight, Math.Ceiling(height))
            : minimumHeight;
    }

    internal static Thickness GetCaptionInsets(Rect bounds, double titleBarHeight)
    {
        if (!double.IsFinite(titleBarHeight) || titleBarHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(titleBarHeight));
        }

        var centeredHeight = bounds.Center.Y * 2;
        return double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) &&
               bounds.Width > 0 && bounds.Height > 0 && double.IsFinite(bounds.Right) &&
               double.IsFinite(centeredHeight) && centeredHeight > 0
            ? new(Math.Max(0, bounds.Right), 0, 0, Math.Max(0, titleBarHeight - centeredHeight))
            : default;
    }

    private static bool IsUsable(MacOsRect bounds)
    {
        return double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width) &&
               double.IsFinite(bounds.Height) && bounds.Width > 0 && bounds.Height > 0;
    }
}
