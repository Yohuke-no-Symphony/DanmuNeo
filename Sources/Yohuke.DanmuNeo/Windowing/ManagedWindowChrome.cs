using Avalonia;
using Avalonia.Controls;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class ManagedWindowChrome(Window window) : IWindowChrome
{
    private bool disposed;

    /// <inheritdoc />
    public void ResizeClient(Size size)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        WindowChrome.ValidateClientSize(size);
        window.Width = size.Width;
        window.Height = size.Height;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        disposed = true;
    }
}
