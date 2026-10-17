using System.Runtime.Versioning;
using Yohuke.DanmuNeo.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Yohuke.DanmuNeo.Windowing;

[SupportedOSPlatform("macos")]
internal sealed class MacOsWindowChrome : IWindowChrome
{
    private readonly Window window;
    private readonly TitleBarView titleBar;
    private readonly double minimumTitleBarHeight;
    private bool disposed;
    private bool refreshing;
    private bool refreshQueued;

    internal MacOsWindowChrome(Window window, TitleBarView titleBar)
    {
        this.window = window;
        this.titleBar = titleBar;
        minimumTitleBarHeight = titleBar.Height;
        if (!double.IsFinite(minimumTitleBarHeight) || minimumTitleBarHeight <= 0)
        {
            throw new ArgumentException("标题栏必须提供正数高度。", nameof(titleBar));
        }

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
        QueueRefresh();
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
            QueueRefresh();
        }
    }

    private void OnTitleBarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Control.HeightProperty && TryGetDecoratedWindow(out _))
        {
            window.ExtendClientAreaTitleBarHeightHint = titleBar.Height;
            QueueRefresh();
        }
    }

    private void RefreshInsets()
    {
        if (refreshing || !TryGetDecoratedWindow(out var handle))
        {
            return;
        }

        refreshing = true;
        try
        {
            var bounds = MacOsCaptionButtons.MeasureBounds(handle);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            var height = MacOsCaptionGeometry.GetTitleBarHeight(minimumTitleBarHeight, bounds);
            titleBar.CaptionInsets = MacOsCaptionGeometry.GetCaptionInsets(bounds, height);
            if (titleBar.Height != height)
            {
                titleBar.Height = height;
            }
        }
        finally
        {
            refreshing = false;
        }
    }

    private bool TryGetDecoratedWindow(out nint handle)
    {
        handle = 0;
        if (disposed || window.WindowState == WindowState.FullScreen ||
            window.TryGetPlatformHandle() is not IPlatformHandle { HandleDescriptor: "NSWindow", Handle: not 0 } platform)
        {
            return false;
        }

        handle = platform.Handle;
        return !MacOsCaptionButtons.IsFullScreen(handle);
    }

    private void QueueRefresh()
    {
        if (disposed || refreshQueued)
        {
            return;
        }

        refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            refreshQueued = false;
            RefreshInsets();
        }, DispatcherPriority.Loaded);
    }
}
