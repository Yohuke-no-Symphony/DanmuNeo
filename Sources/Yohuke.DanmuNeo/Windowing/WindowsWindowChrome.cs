using Yohuke.DanmuNeo.I18n;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Yohuke.DanmuNeo.Windowing;

[SupportedOSPlatform("windows")]
internal sealed class WindowsWindowChrome : IWindowChrome
{
    private readonly Window window;
    private readonly IWindowTitleBar titleBar;
    private readonly nint handle;
    private readonly Win32Properties.CustomWndProcHookCallback callback;
    private readonly double minimumTitleBarHeight;
    private readonly WindowsCaptionSurface captionSurface;
    private readonly WindowContentSizing contentSizing;
    private Size? openingClientSize;
    private Size? deferredClientSize;
    private bool disposed;
    private bool refreshQueued;
    private bool frameRefreshRequested;
    private bool applyingFrame;
    private bool inputMethodRecoveryQueued;
    private TextBox? invalidatedCompositionInput;
    private TextBox? deferredCompositionInput;

    internal WindowsWindowChrome(Window window, IWindowTitleBar titleBar)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(titleBar);
        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle is not { HandleDescriptor: "HWND", Handle: not 0 })
        {
            throw new ArgumentException(Localization.Get("Window.Chrome.InvalidWindowsHandle"), nameof(window));
        }

        this.window = window;
        this.titleBar = titleBar;
        handle = platformHandle.Handle;
        minimumTitleBarHeight = titleBar.View.Height;
        if (!double.IsFinite(minimumTitleBarHeight) || minimumTitleBarHeight <= 0)
        {
            throw new ArgumentException(Localization.Get("Window.Chrome.InvalidTitleBarHeight"), nameof(titleBar));
        }

        if (window.SizeToContent == SizeToContent.Manual && double.IsFinite(window.Width) && window.Width > 0 &&
            double.IsFinite(window.Height) && window.Height > 0)
        {
            openingClientSize = new(window.Width, window.Height);
        }

        window.SystemDecorations = SystemDecorations.Full;
        window.ExtendClientAreaToDecorationsHint = false;
        captionSurface = new(window);
        callback = WindowProcedure;
        Win32Properties.AddWndProcHookCallback(window, callback);
        contentSizing = new(window, () => QueueRefresh(false), ResizeClient, ReadClientSize, ReportError);
        window.Opened += OnOpened;
        window.Closed += OnClosed;
        window.Resized += OnResized;
        window.LayoutUpdated += OnLayoutUpdated;
        window.ScalingChanged += OnScalingChanged;
        window.PropertyChanged += OnWindowPropertyChanged;
        window.Activated += OnActivated;
        window.AddHandler(InputElement.GotFocusEvent, OnInputGotFocus, RoutingStrategies.Bubble);
        InputMethod.AddTextInputMethodClientRequeryRequestedHandler(window, OnInputMethodRequeryRequested);
        window.AddHandler(WorkbenchTextInput.NativeContextInvalidatedEvent, OnNativeInputContextInvalidated, RoutingStrategies.Bubble);
        titleBar.View.PropertyChanged += OnTitleBarPropertyChanged;
        try
        {
            ApplyFrame();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal Exception? LastError { get; private set; }
    internal bool CaptionButtonsMeasured { get; private set; }
    internal bool NonClientRenderingEnabled { get; private set; }
    internal Rect CaptionAperture => captionSurface.Aperture;

    /// <inheritdoc />
    public void ResizeClient(Size size)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (LastError is { } error)
        {
            throw new InvalidOperationException(Localization.Get("Window.Chrome.ClientSizeFailed"), error);
        }

        var outerSize = WindowsChromeGeometry.GetOuterSize(size, ReadDpi(), ReadFrameInsets());
        if (window.WindowState == WindowState.FullScreen)
        {
            deferredClientSize = size;
            return;
        }

        if (WindowsNativeMethods.IsIconic(handle) != 0 || WindowsNativeMethods.IsZoomed(handle) != 0)
        {
            var placement = new WindowsWindowPlacement
            {
                Length = checked((uint)Marshal.SizeOf<WindowsWindowPlacement>())
            };
            CheckWin32(WindowsNativeMethods.GetWindowPlacement(handle, ref placement));
            var normal = placement.NormalPosition;
            normal.Right = checked(normal.Left + outerSize.Width);
            normal.Bottom = checked(normal.Top + outerSize.Height);
            placement.NormalPosition = normal;
            CheckWin32(WindowsNativeMethods.SetWindowPlacement(handle, in placement));
        }
        else
        {
            CheckWin32(WindowsNativeMethods.SetWindowPos(handle, 0, 0, 0, outerSize.Width, outerSize.Height,
                WindowsNativeMethods.SWP_NOMOVE | WindowsNativeMethods.SWP_NOZORDER |
                WindowsNativeMethods.SWP_NOACTIVATE));
        }

        QueueRefresh(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Win32Properties.RemoveWndProcHookCallback(window, callback);
        window.Opened -= OnOpened;
        window.Closed -= OnClosed;
        window.Resized -= OnResized;
        window.LayoutUpdated -= OnLayoutUpdated;
        window.ScalingChanged -= OnScalingChanged;
        window.PropertyChanged -= OnWindowPropertyChanged;
        window.Activated -= OnActivated;
        window.RemoveHandler(InputElement.GotFocusEvent, OnInputGotFocus);
        InputMethod.RemoveTextInputMethodClientRequeryRequestedHandler(window, OnInputMethodRequeryRequested);
        window.RemoveHandler(WorkbenchTextInput.NativeContextInvalidatedEvent, OnNativeInputContextInvalidated);
        invalidatedCompositionInput = null;
        deferredCompositionInput = null;
        titleBar.View.PropertyChanged -= OnTitleBarPropertyChanged;
        contentSizing.Dispose();
        captionSurface.Dispose();
    }

    private unsafe nint WindowProcedure(nint nativeWindow, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (disposed || LastError is not null)
        {
            return 0;
        }

        try
        {
            switch (message)
            {
                case WindowsNativeMethods.WM_SETFOCUS:
                    QueueInputMethodRecovery();
                    return 0;
                case WindowsNativeMethods.WM_IME_ENDCOMPOSITION:
                    if (deferredCompositionInput is not null)
                    {
                        deferredCompositionInput = null;
                        QueueInputMethodRecovery();
                    }

                    return 0;
                case WindowsNativeMethods.WM_NCCALCSIZE:
                    if (window.WindowState != WindowState.FullScreen && WindowsNativeMethods.IsIconic(handle) == 0)
                    {
                        var frame = ReadFrameInsets();
                        var maximized = WindowsNativeMethods.IsZoomed(handle) != 0;
                        if (wParam != 0)
                        {
                            var parameters = (WindowsNccalcSizeParameters*)lParam;
                            parameters->NewWindow = WindowsChromeGeometry.GetClientRect(parameters->NewWindow, frame,
                                maximized);
                        }
                        else
                        {
                            var rect = (WindowsRect*)lParam;
                            *rect = WindowsChromeGeometry.GetClientRect(*rect, frame, maximized);
                        }

                        handled = true;
                    }

                    return 0;
                case WindowsNativeMethods.WM_GETMINMAXINFO:
                    if (window.WindowState != WindowState.FullScreen)
                    {
                        ApplyTrackingSizes((WindowsMinMaxInfo*)lParam);
                        handled = true;
                    }

                    return 0;
                case WindowsNativeMethods.WM_NCHITTEST:
                    if (window.WindowState == WindowState.FullScreen)
                    {
                        return 0;
                    }

                    handled = true;
                    return HitTest(nativeWindow, message, wParam, lParam);
                case WindowsNativeMethods.WM_NCMOUSEMOVE:
                case WindowsNativeMethods.WM_NCLBUTTONDOWN:
                case WindowsNativeMethods.WM_NCLBUTTONUP:
                case WindowsNativeMethods.WM_NCLBUTTONDBLCLK:
                case WindowsNativeMethods.WM_NCRBUTTONDOWN:
                case WindowsNativeMethods.WM_NCRBUTTONUP:
                case WindowsNativeMethods.WM_NCMOUSELEAVE:
                    if (WindowsNativeMethods.DwmDefWindowProc(nativeWindow, message, wParam, lParam, out var result) != 0)
                    {
                        handled = true;
                        return result;
                    }

                    return 0;
                case WindowsNativeMethods.WM_DPICHANGED:
                case WindowsNativeMethods.WM_ACTIVATE:
                case WindowsNativeMethods.WM_DWMCOMPOSITIONCHANGED:
                case WindowsNativeMethods.WM_THEMECHANGED:
                    QueueRefresh(true);
                    return 0;
                default:
                    return 0;
            }
        }
        catch (Exception error) when (error is Win32Exception or COMException or OverflowException or ArgumentException)
        {
            LastError = error;
            handled = false;
            Trace.TraceError(Localization.Get("Window.Chrome.WindowsFailed"), error);
            return 0;
        }
    }

    private unsafe nint HitTest(nint nativeWindow, uint message, nint wParam, nint lParam)
    {
        if (WindowsNativeMethods.DwmDefWindowProc(nativeWindow, message, wParam, lParam, out var result) != 0 &&
            result != (nint)WindowsChromeHitTest.CLIENT && result != (nint)WindowsChromeHitTest.NONE)
        {
            return result;
        }

        var point = WindowsChromeGeometry.GetScreenPoint(lParam);
        var nativeTitleBar = new WindowsTitleBarInfoEx { Size = checked((uint)sizeof(WindowsTitleBarInfoEx)) };
        WindowsNativeMethods.DefWindowProc(handle, WindowsNativeMethods.WM_GETTITLEBARINFOEX, 0,
            (nint)(&nativeTitleBar));
        var captionHit = WindowsChromeGeometry.HitCaptionButtons(point, in nativeTitleBar);
        if (captionHit != WindowsChromeHitTest.NONE)
        {
            return (nint)captionHit;
        }

        CheckWin32(WindowsNativeMethods.GetWindowRect(handle, out var outer));
        var resize = WindowsChromeGeometry.HitResizeFrame(point, outer, ReadFrameInsets(), window.CanResize,
            WindowsNativeMethods.IsZoomed(handle) != 0);
        if (resize != WindowsChromeHitTest.NONE)
        {
            return (nint)resize;
        }

        CheckWin32(WindowsNativeMethods.ScreenToClient(handle, ref point));
        var scale = ReadDpi() / WindowsChromeGeometry.STANDARD_DPI;
        return titleBar.IsDragRegion(new(point.X / scale, point.Y / scale))
            ? (nint)WindowsChromeHitTest.CAPTION
            : (nint)WindowsChromeHitTest.CLIENT;
    }

    private unsafe void ApplyTrackingSizes(WindowsMinMaxInfo* sizes)
    {
        var frame = ReadFrameInsets();
        var dpi = ReadDpi();
        var minimum = sizes->MinTrackSize;
        var maximum = sizes->MaxTrackSize;
        if (window.MinWidth > 0)
        {
            minimum.X = checked(WindowsChromeGeometry.ToPixels(window.MinWidth, dpi) + frame.Left + frame.Right);
        }

        if (window.MinHeight > 0)
        {
            minimum.Y = checked(WindowsChromeGeometry.ToPixels(window.MinHeight, dpi) + frame.Bottom);
        }

        if (double.IsFinite(window.MaxWidth) && window.MaxWidth > 0)
        {
            maximum.X = checked(WindowsChromeGeometry.ToPixels(window.MaxWidth, dpi) + frame.Left + frame.Right);
        }

        if (double.IsFinite(window.MaxHeight) && window.MaxHeight > 0)
        {
            maximum.Y = checked(WindowsChromeGeometry.ToPixels(window.MaxHeight, dpi) + frame.Bottom);
        }

        sizes->MinTrackSize = minimum;
        sizes->MaxTrackSize = maximum;
    }

    private uint ReadDpi()
    {
        var dpi = WindowsNativeMethods.GetDpiForWindow(handle);
        if (dpi == 0)
        {
            throw new Win32Exception(Localization.Get("Window.Chrome.ReadDpiFailed"));
        }

        return dpi;
    }

    private Size ReadClientSize()
    {
        CheckWin32(WindowsNativeMethods.GetClientRect(handle, out var rect));
        var scale = ReadDpi() / WindowsChromeGeometry.STANDARD_DPI;
        return new((rect.Right - rect.Left) / scale, (rect.Bottom - rect.Top) / scale);
    }

    private WindowsFrameInsets ReadFrameInsets()
    {
        var style = unchecked((uint)WindowsNativeMethods.GetWindowLongPtr(handle, WindowsNativeMethods.GWL_STYLE));
        var extendedStyle = unchecked((uint)WindowsNativeMethods.GetWindowLongPtr(handle,
            WindowsNativeMethods.GWL_EXSTYLE));
        style &= ~(WindowsNativeMethods.WS_CAPTION | WindowsNativeMethods.WS_MINIMIZE | WindowsNativeMethods.WS_MAXIMIZE);
        var frame = new WindowsRect();
        CheckWin32(WindowsNativeMethods.AdjustWindowRectExForDpi(ref frame, style, 0, extendedStyle, ReadDpi()));
        return new(-frame.Left, -frame.Top, frame.Right, frame.Bottom);
    }

    private void ApplyFrame()
    {
        applyingFrame = true;
        try
        {
            CheckWin32(WindowsNativeMethods.SetWindowPos(handle, 0, 0, 0, 0, 0,
                WindowsNativeMethods.SWP_NOMOVE | WindowsNativeMethods.SWP_NOSIZE | WindowsNativeMethods.SWP_NOZORDER |
                WindowsNativeMethods.SWP_NOACTIVATE | WindowsNativeMethods.SWP_FRAMECHANGED));
            var policy = WindowsNativeMethods.DWMNCRP_ENABLED;
            Marshal.ThrowExceptionForHR(WindowsNativeMethods.DwmSetWindowAttribute(handle,
                WindowsNativeMethods.DWMWA_NCRENDERING_POLICY, in policy, sizeof(int)));
            var dark = window.ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                Marshal.ThrowExceptionForHR(WindowsNativeMethods.DwmSetWindowAttribute(handle,
                    WindowsNativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, in dark, sizeof(int)));
            }

            var margins = new WindowsMargins
            {
                TopHeight = window.WindowState == WindowState.FullScreen
                    ? 0
                    : WindowsChromeGeometry.ToPixels(titleBar.View.Height, ReadDpi())
            };
            Marshal.ThrowExceptionForHR(WindowsNativeMethods.DwmExtendFrameIntoClientArea(handle, in margins));
            Marshal.ThrowExceptionForHR(WindowsNativeMethods.DwmGetWindowAttribute(handle,
                WindowsNativeMethods.DWMWA_NCRENDERING_ENABLED, out int enabled, sizeof(int)));
            NonClientRenderingEnabled = enabled != 0;
        }
        finally
        {
            applyingFrame = false;
        }
    }

    private void RefreshCaptionGeometry()
    {
        if (window.WindowState == WindowState.FullScreen)
        {
            captionSurface.Update(default);
            return;
        }

        if (WindowsNativeMethods.IsWindowVisible(handle) == 0 || WindowsNativeMethods.IsIconic(handle) != 0 ||
            window.ClientSize.Width <= 0 || window.ClientSize.Height <= 0)
        {
            return;
        }

        var status = WindowsNativeMethods.DwmGetWindowAttribute(handle, WindowsNativeMethods.DWMWA_CAPTION_BUTTON_BOUNDS,
            out WindowsRect bounds, checked((uint)Marshal.SizeOf<WindowsRect>()));
        CaptionButtonsMeasured = status >= 0 && WindowsChromeGeometry.IsUsableCaptionBounds(bounds);
        if (!CaptionButtonsMeasured)
        {
            return;
        }

        CheckWin32(WindowsNativeMethods.GetWindowRect(handle, out var outer));
        CheckWin32(WindowsNativeMethods.GetClientRect(handle, out var client));
        var origin = new WindowsPoint();
        CheckWin32(WindowsNativeMethods.ClientToScreen(handle, ref origin));
        var dpi = ReadDpi();
        titleBar.CaptionInsets = WindowsChromeGeometry.GetCaptionInsets(outer, origin, client.Right - client.Left,
            bounds, dpi);
        var captionBottom = (long)outer.Top + bounds.Bottom - origin.Y;
        titleBar.View.Height = Math.Max(minimumTitleBarHeight, captionBottom * WindowsChromeGeometry.STANDARD_DPI / dpi);
        captionSurface.Update(WindowsChromeGeometry.GetCaptionAperture(outer, origin, window.ClientSize, bounds, dpi));
    }

    private void QueueRefresh(bool frame)
    {
        if (disposed)
        {
            return;
        }

        frameRefreshRequested |= frame;
        if (refreshQueued)
        {
            return;
        }

        refreshQueued = true;
        Dispatcher.UIThread.Post(RefreshQueued, DispatcherPriority.Loaded);
    }

    private void RefreshQueued()
    {
        refreshQueued = false;
        if (disposed || LastError is not null)
        {
            return;
        }

        var applyFrame = frameRefreshRequested;
        frameRefreshRequested = false;
        try
        {
            if (applyFrame)
            {
                ApplyFrame();
            }

            if (deferredClientSize is { } size && window.WindowState == WindowState.Normal)
            {
                deferredClientSize = null;
                ResizeClient(size);
            }

            RefreshCaptionGeometry();
            contentSizing.FitToContent();
        }
        catch (Exception error) when (error is Win32Exception or COMException or OverflowException or ArgumentException)
        {
            ReportError(error);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (openingClientSize is { } size)
        {
            openingClientSize = null;
            deferredClientSize = size;
        }

        frameRefreshRequested = true;
        RefreshQueued();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        QueueInputMethodRecovery();
    }

    private void OnInputGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is TextBox)
        {
            QueueInputMethodRecovery();
        }
    }

    private void OnInputMethodRequeryRequested(object? sender, RoutedEventArgs e)
    {
        QueueInputMethodRecovery();
    }

    private void OnNativeInputContextInvalidated(object? sender, RoutedEventArgs e)
    {
        if (window.FocusManager?.GetFocusedElement() is TextBox input &&
            ReferenceEquals(TopLevel.GetTopLevel(input), window) && WorkbenchTextInput.IsComposing(input))
        {
            invalidatedCompositionInput = input;
        }

        QueueInputMethodRecovery();
    }

    private void QueueInputMethodRecovery()
    {
        if (disposed || inputMethodRecoveryQueued)
        {
            return;
        }

        inputMethodRecoveryQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            inputMethodRecoveryQueued = false;
            if (!disposed)
            {
                var focused = window.FocusManager?.GetFocusedElement();
                var cancelComposition = invalidatedCompositionInput is not null &&
                                        ReferenceEquals(invalidatedCompositionInput, focused);
                deferredCompositionInput = !cancelComposition && WorkbenchTextInput.IsComposing(focused)
                    ? focused as TextBox
                    : null;
                if (WorkbenchTextInput.RestoreInputMethod(window, () => RestoreNativeInputWindow(cancelComposition), cancelComposition) ||
                    !ReferenceEquals(invalidatedCompositionInput, focused))
                {
                    invalidatedCompositionInput = null;
                }
            }
        }, DispatcherPriority.Background);
    }

    private bool RestoreNativeInputWindow(bool cancelComposition)
    {
        if (WindowsNativeMethods.GetActiveWindow() != handle || WindowsNativeMethods.GetFocus() != handle)
        {
            return false;
        }

        var keyboardLayout = WindowsNativeMethods.GetKeyboardLayout(0);
        if (keyboardLayout == 0)
        {
            return false;
        }

        if (cancelComposition && !CancelNativeComposition())
        {
            return false;
        }

        WindowsNativeMethods.SendMessage(handle, WindowsNativeMethods.WM_INPUTLANGCHANGE, 0, keyboardLayout);
        return true;
    }

    private bool CancelNativeComposition()
    {
        var context = WindowsNativeMethods.ImmGetContext(handle);
        if (context == 0)
        {
            return true;
        }

        try
        {
            var length = WindowsNativeMethods.ImmGetCompositionString(context, WindowsNativeMethods.GCS_COMPSTR, 0, 0);
            return WindowsNativeMethods.ImmNotifyIME(context, WindowsNativeMethods.NI_COMPOSITIONSTR, WindowsNativeMethods.CPS_CANCEL, 0) != 0 ||
                   length is 0 or WindowsNativeMethods.IMM_ERROR_NODATA;
        }
        finally
        {
            WindowsNativeMethods.ImmReleaseContext(handle, context);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Dispose();
    }

    private void OnResized(object? sender, WindowResizedEventArgs e)
    {
        contentSizing.OnResized(e.ClientSize, e.Reason);
        if (!applyingFrame)
        {
            QueueRefresh(true);
        }
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        contentSizing.InvalidateMeasure();
        QueueRefresh(true);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (contentSizing.CanFitToContent)
        {
            QueueRefresh(false);
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Window.CanResizeProperty ||
            e.Property == Window.CanMinimizeProperty || e.Property == Window.CanMaximizeProperty ||
            e.Property == TopLevel.ActualThemeVariantProperty)
        {
            QueueRefresh(true);
        }
        else if (e.Property == Window.SizeToContentProperty)
        {
            QueueRefresh(false);
        }
    }

    private void OnTitleBarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Layoutable.HeightProperty)
        {
            QueueRefresh(true);
        }
    }

    private static void CheckWin32(int result)
    {
        if (result == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private void ReportError(Exception error)
    {
        LastError = error;
        Trace.TraceError(Localization.Get("Window.Chrome.WindowsRefreshFailed"), error);
    }
}
