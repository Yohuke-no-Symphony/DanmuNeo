using Yohuke.DanmuNeo.I18n;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Yohuke.DanmuNeo.Windowing;

[SupportedOSPlatform("macos")]
internal sealed class MacOsWindowChrome : IWindowChrome
{
    private readonly Window window;
    private readonly IWindowTitleBar titleBar;
    private readonly double minimumTitleBarHeight;
    private readonly WindowContentSizing contentSizing;
    private bool disposed;
    private bool refreshing;
    private bool refreshQueued;

    internal MacOsWindowChrome(Window window, IWindowTitleBar titleBar)
    {
        this.window = window;
        this.titleBar = titleBar;
        minimumTitleBarHeight = titleBar.View.Height;
        if (!double.IsFinite(minimumTitleBarHeight) || minimumTitleBarHeight <= 0)
        {
            throw new ArgumentException(Localization.Get("Window.Chrome.InvalidTitleBarHeight"), nameof(titleBar));
        }

        window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.SystemChrome |
                                            ExtendClientAreaChromeHints.OSXThickTitleBar;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaTitleBarHeightHint = titleBar.View.Height;
        contentSizing = new(window, QueueRefresh, ResizeClient, () => window.ClientSize);
        window.Opened += OnOpened;
        window.PropertyChanged += OnWindowPropertyChanged;
        window.ScalingChanged += OnScalingChanged;
        window.LayoutUpdated += OnLayoutUpdated;
        window.Resized += OnResized;
        window.Closed += OnClosed;
        titleBar.View.PropertyChanged += OnTitleBarPropertyChanged;
        RefreshInsets();
    }

    /// <inheritdoc />
    public void ResizeClient(Size size)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        WindowChrome.ValidateClientSize(size);
        window.Width = size.Width;
        window.Height = size.Height;
        window.UpdateLayout();
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
        window.ScalingChanged -= OnScalingChanged;
        window.LayoutUpdated -= OnLayoutUpdated;
        window.Resized -= OnResized;
        window.Closed -= OnClosed;
        titleBar.View.PropertyChanged -= OnTitleBarPropertyChanged;
        contentSizing.Dispose();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        RefreshInsets();
        window.UpdateLayout();
        contentSizing.FitToContent();
        QueueRefresh();
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        contentSizing.InvalidateMeasure();
        QueueRefresh();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (contentSizing.CanFitToContent)
        {
            QueueRefresh();
        }
    }

    private void OnResized(object? sender, WindowResizedEventArgs e)
    {
        contentSizing.OnResized(e.ClientSize, e.Reason);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Dispose();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Window.WindowDecorationMarginProperty ||
            e.Property == TopLevel.ClientSizeProperty)
        {
            QueueRefresh();
        }
    }

    private void OnTitleBarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Layoutable.HeightProperty && TryGetDecoratedWindow(out _))
        {
            window.ExtendClientAreaTitleBarHeightHint = titleBar.View.Height;
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
            if (!titleBar.View.Height.Equals(height))
            {
                titleBar.View.Height = height;
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
            window.TryGetPlatformHandle() is not { HandleDescriptor: "NSWindow", Handle: not 0 } platform)
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
            contentSizing.FitToContent();
        }, DispatcherPriority.Loaded);
    }
}
