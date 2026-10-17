using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class WindowContentSizing : IDisposable
{
    private readonly Window window;
    private readonly Action requestRefresh;
    private readonly Action<Size> resizeClient;
    private readonly Func<Size> readClientSize;
    private readonly Action<Exception>? reportError;
    private WindowContentSizingPresenter? presenter;
    private SizeToContent mode;
    private Size fixedClientSize;
    private bool changingContent;
    private bool changingMode;
    private bool applyingSize;
    private bool disposed;

    internal WindowContentSizing(Window window, Action requestRefresh, Action<Size> resizeClient,
        Func<Size> readClientSize, Action<Exception>? reportError = null)
    {
        this.window = window;
        this.requestRefresh = requestRefresh;
        this.resizeClient = resizeClient;
        this.readClientSize = readClientSize;
        this.reportError = reportError;
        window.PropertyChanged += OnWindowPropertyChanged;
        SetMode(window.SizeToContent);
    }

    internal SizeToContent RequestedMode
    {
        get => mode;
        set => SetMode(value);
    }

    internal Exception? LastError { get; private set; }

    internal bool CanFitToContent => !disposed && LastError is null && mode != SizeToContent.Manual &&
        presenter is { HasMeasured: true, IsMeasureValid: true } && window.IsVisible &&
        window.WindowState == WindowState.Normal;

    internal bool NeedsResize
    {
        get
        {
            if (!CanFitToContent || !TryReadClientSize(out var clientSize))
            {
                return false;
            }

            return !HaveSamePixels(presenter!.DesiredClientSize, clientSize);
        }
    }

    internal void FitToContent()
    {
        if (!NeedsResize)
        {
            return;
        }

        applyingSize = true;
        try
        {
            resizeClient(presenter!.DesiredClientSize);
        }
        finally
        {
            applyingSize = false;
        }
    }

    internal void InvalidateMeasure()
    {
        UpdateConstraints();
        presenter?.InvalidateMeasure();
        requestRefresh();
    }

    internal void OnResized(Size clientSize, WindowResizeReason reason)
    {
        if (mode == SizeToContent.Manual || applyingSize || !window.CanResize ||
            reason != WindowResizeReason.User || presenter is not { HasMeasured: true })
        {
            return;
        }

        var desired = presenter.DesiredClientSize;
        var nextMode = mode;
        if (!HaveSamePixels(new(desired.Width, 1), new(clientSize.Width, 1)))
        {
            nextMode &= ~SizeToContent.Width;
        }

        if (!HaveSamePixels(new(1, desired.Height), new(1, clientSize.Height)))
        {
            nextMode &= ~SizeToContent.Height;
        }

        if (nextMode != mode)
        {
            SetMode(nextMode);
        }

        if (mode != SizeToContent.Manual)
        {
            fixedClientSize = new(
                mode.HasFlag(SizeToContent.Width) ? fixedClientSize.Width : clientSize.Width,
                mode.HasFlag(SizeToContent.Height) ? fixedClientSize.Height : clientSize.Height);
            UpdateConstraints();
            requestRefresh();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        window.PropertyChanged -= OnWindowPropertyChanged;
    }

    private void SetMode(SizeToContent value)
    {
        if (disposed)
        {
            return;
        }

        mode = value;
        if (value == SizeToContent.Manual)
        {
            RemovePresenter();
            return;
        }

        fixedClientSize = new(
            GetRequestedDimension(window.Width, window.ClientSize.Width),
            GetRequestedDimension(window.Height, window.ClientSize.Height));
        changingMode = true;
        try
        {
            window.SetCurrentValue(Window.SizeToContentProperty, SizeToContent.Manual);
        }
        finally
        {
            changingMode = false;
        }

        InstallPresenter();
        UpdateConstraints();
        requestRefresh();
    }

    private void InstallPresenter()
    {
        if (ReferenceEquals(window.Content, presenter) && presenter is not null)
        {
            return;
        }

        changingContent = true;
        try
        {
            var content = window.Content;
            window.Content = null;
            presenter = new(content as Control ?? new ContentPresenter { Content = content });
            window.Content = presenter;
        }
        finally
        {
            changingContent = false;
        }
    }

    private void RemovePresenter()
    {
        if (presenter is null)
        {
            return;
        }

        changingContent = true;
        try
        {
            if (ReferenceEquals(window.Content, presenter))
            {
                var content = presenter.Child;
                presenter.Child = null;
                window.Content = content;
            }

            presenter = null;
        }
        finally
        {
            changingContent = false;
        }
    }

    private void UpdateConstraints()
    {
        presenter?.UpdateConstraints(mode, fixedClientSize, new(window.MinWidth, window.MinHeight),
            new(window.MaxWidth, window.MaxHeight), window.Padding + window.BorderThickness);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (LastError is not null)
        {
            return;
        }

        if (e.Property == Window.SizeToContentProperty && !changingMode)
        {
            SetMode(window.SizeToContent);
        }
        else if (mode != SizeToContent.Manual)
        {
            if (e.Property == ContentControl.ContentProperty && !changingContent)
            {
                InstallPresenter();
                UpdateConstraints();
                requestRefresh();
            }
            else if (e.Property == Layoutable.MinWidthProperty || e.Property == Layoutable.MinHeightProperty ||
                     e.Property == Layoutable.MaxWidthProperty || e.Property == Layoutable.MaxHeightProperty ||
                     e.Property == TemplatedControl.PaddingProperty || e.Property == TemplatedControl.BorderThicknessProperty)
            {
                UpdateConstraints();
                requestRefresh();
            }
            else if (!applyingSize && (e.Property == Layoutable.WidthProperty || e.Property == Layoutable.HeightProperty))
            {
                UpdateFixedDimension(e);
            }
        }
    }

    private void UpdateFixedDimension(AvaloniaPropertyChangedEventArgs e)
    {
        var isWidth = e.Property == Layoutable.WidthProperty;
        if (mode.HasFlag(isWidth ? SizeToContent.Width : SizeToContent.Height))
        {
            return;
        }

        var value = isWidth ? window.Width : window.Height;
        if (!double.IsFinite(value) || value <= 0)
        {
            return;
        }

        if (!TryReadClientSize(out var native))
        {
            return;
        }

        if (HaveSamePixels(new(value, 1), new(isWidth ? native.Width : native.Height, 1)))
        {
            return;
        }

        fixedClientSize = isWidth ? fixedClientSize.WithWidth(value) : fixedClientSize.WithHeight(value);
        UpdateConstraints();
        requestRefresh();
    }

    private bool TryReadClientSize(out Size clientSize)
    {
        try
        {
            clientSize = readClientSize();
            return true;
        }
        catch (Exception error) when (error is Win32Exception or COMException or OverflowException or ArgumentException)
        {
            LastError = error;
            reportError?.Invoke(error);
            clientSize = default;
            return false;
        }
    }

    private bool HaveSamePixels(Size first, Size second)
    {
        var scaling = window.RenderScaling;
        return WindowSizingGeometry.ToPixels(first.Width, scaling) == WindowSizingGeometry.ToPixels(second.Width, scaling) &&
               WindowSizingGeometry.ToPixels(first.Height, scaling) == WindowSizingGeometry.ToPixels(second.Height, scaling);
    }

    private static double GetRequestedDimension(double requested, double current)
    {
        return double.IsFinite(requested) && requested > 0 ? requested : Math.Max(1, current);
    }
}
