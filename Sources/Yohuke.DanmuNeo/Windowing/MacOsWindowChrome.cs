using System.Runtime.Versioning;
using Yohuke.DanmuNeo.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Yohuke.DanmuNeo.Windowing;

[SupportedOSPlatform("macos")]
internal sealed class MacOsWindowChrome : IWindowChrome
{
    private readonly Window window;
    private readonly TitleBarView titleBar;
    private bool disposed;

    internal MacOsWindowChrome(Window window, TitleBarView titleBar)
    {
        this.window = window;
        this.titleBar = titleBar;
        window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.SystemChrome |
                                            ExtendClientAreaChromeHints.OSXThickTitleBar;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = titleBar.Height;
        window.Opened += OnOpened;
        window.PropertyChanged += OnWindowPropertyChanged;
        window.ScalingChanged += OnOpened;
        window.Closed += OnClosed;
        titleBar.PropertyChanged += OnTitleBarPropertyChanged;
        RefreshInsets();
    }

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
        if (disposed)
        {
            return;
        }

        disposed = true;
        window.Opened -= OnOpened;
        window.PropertyChanged -= OnWindowPropertyChanged;
        window.ScalingChanged -= OnOpened;
        window.Closed -= OnClosed;
        titleBar.PropertyChanged -= OnTitleBarPropertyChanged;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        RefreshInsets();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Dispose();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Window.WindowDecorationMarginProperty ||
            e.Property == Window.ClientSizeProperty)
        {
            RefreshInsets();
        }
    }

    private void OnTitleBarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Control.HeightProperty)
        {
            window.ExtendClientAreaTitleBarHeightHint = titleBar.Height;
            RefreshInsets();
        }
    }

    private void RefreshInsets()
    {
        if (!disposed && window.TryGetPlatformHandle() is IPlatformHandle { HandleDescriptor: "NSWindow", Handle: not 0 } handle)
        {
            titleBar.CaptionInsets = new(MacOsCaptionButtons.MeasureLeftInset(handle.Handle), 0, 0, 0);
        }
    }
}
