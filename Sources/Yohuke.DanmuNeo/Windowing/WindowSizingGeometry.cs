namespace Yohuke.DanmuNeo.Windowing;

internal static class WindowSizingGeometry
{
    internal static int ToPixels(double value, double scaling)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (!double.IsFinite(scaling) || scaling <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaling));
        }

        var scaled = value * scaling;
        var nearest = Math.Round(scaled);
        if (nearest > 0 && scaled >= Math.BitDecrement(Math.BitDecrement(nearest)) &&
            scaled <= Math.BitIncrement(Math.BitIncrement(nearest)))
        {
            return checked((int)nearest);
        }

        return checked((int)Math.Ceiling(scaled));
    }
}
