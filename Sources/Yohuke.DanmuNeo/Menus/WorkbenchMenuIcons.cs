using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lucide.Avalonia;

namespace Yohuke.DanmuNeo.Menus;

internal sealed class WorkbenchMenuIcons : IDisposable
{
    private readonly Dictionary<LucideIconKind, Bitmap> nativeIcons = [];
    private bool disposed;

    internal static LucideIcon Create(LucideIconKind kind)
    {
        return new() { Kind = kind, Size = 16, StrokeWidth = 1.8 };
    }

    internal Bitmap GetNative(LucideIconKind kind)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (nativeIcons.TryGetValue(kind, out var existing))
        {
            return existing;
        }

        var geometry = Geometry.Parse(kind.GetGeometryData());
        var stroke = new Pen(new SolidColorBrush(Color.FromRgb(128, 128, 128)), 1.8,
            lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var icon = new RenderTargetBitmap(new(24, 24));

        using (var context = icon.CreateDrawingContext())
        {
            context.DrawGeometry(null, stroke, geometry);
        }

        nativeIcons.Add(kind, icon);
        return icon;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        foreach (var icon in nativeIcons.Values)
        {
            icon.Dispose();
        }

        nativeIcons.Clear();
    }
}
