using System.Text.Json;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Captures;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

public partial class LivePlayerPanelView
{
    private readonly ILiveFrameCaptureService? frameCaptureService;
    private CancellationTokenSource? captureHostLifetime = new();
    private string? captureGesture;

    /// <summary>注入原帧桥接服务，保留页面和原生宿主的职责边界。</summary>
    public LivePlayerPanelView(WorkbenchContext context, ILiveFrameCaptureService frameCaptureService) : this(context)
    {
        this.frameCaptureService = frameCaptureService;
        frameCaptureService.CaptureShortcutRequested += OnNativeCaptureShortcut;
    }

    /// <summary>玩家按钮及原生网页焦点下均请求同一个应用动作。</summary>
    public event EventHandler? CaptureRequested;

    /// <summary>原生播放器挂载、释放或切换后通知工作台更新抓帧入口。</summary>
    public event EventHandler? CaptureAvailabilityChanged;

    /// <summary>当前拥有有效来源和已挂载的原生播放器宿主。</summary>
    public bool CanCaptureFrame => !isDisposed && isAttached && liveWebView is not null &&
                                   Context.CurrentRoom is { IsLivePlayerVisible: true } room &&
                                   TryGetPlayerToken(room.LivePlayerUrl, out _);

    private void NotifyCaptureAvailability()
    {
        CaptureAvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>从当前原生播放器获得视频原帧，切房或宿主替换后拒绝迟到结果。</summary>
    public async Task<LiveCapturedFrame> CaptureFrameAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (frameCaptureService is null || liveWebView is null || !isAttached || isDisposed ||
            Context.CurrentRoom is not { IsLivePlayerVisible: true } room ||
            !TryGetPlayerToken(room.LivePlayerUrl, out var token))
        {
            throw new InvalidOperationException(Localization.Get("Captures.NoFrame"));
        }
        var selection = Context.CaptureSelection();
        var generation = hostGeneration;
        var view = liveWebView;
        var url = room.LivePlayerUrl;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, captureHostLifetime!.Token);
        var frame = await frameCaptureService.RequestFrameAsync(token, async script =>
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lifetime.Token.ThrowIfCancellationRequested();
                if (generation != hostGeneration || !ReferenceEquals(view, liveWebView) || !Context.IsCurrent(selection) ||
                    Context.CurrentRoom?.LivePlayerUrl != url)
                {
                    throw new OperationCanceledException(Localization.Get("Captures.SessionChanged"));
                }
                view.InvokeScript(script);
            });
        }, lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        if (generation != hostGeneration || !ReferenceEquals(view, liveWebView) || !Context.IsCurrent(selection) ||
            Context.CurrentRoom?.LivePlayerUrl != url)
        {
            throw new OperationCanceledException(Localization.Get("Captures.SessionChanged"));
        }
        return frame;
    }

    /// <summary>同步经过应用冲突检测的实际绑定；空值停用原生网页快捷键。</summary>
    public void SetCaptureGesture(string? gestureText)
    {
        captureGesture = ShortcutBindingService.TryNormalizeGestureText(gestureText, out var normalized) ? normalized : null;
        SyncCaptureGesture();
    }

    private void Capture_OnClick(object? sender, RoutedEventArgs e)
    {
        CaptureRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnNativeCaptureShortcut(object? sender, LiveCaptureShortcutEventArgs e)
    {
        var shortcutToken = e.Token;
        Dispatcher.UIThread.Post(() =>
        {
            if (isDisposed || !isAttached || string.IsNullOrEmpty(captureGesture) ||
                Context.CurrentRoom is not { IsLivePlayerVisible: true } room ||
                !TryGetPlayerToken(room.LivePlayerUrl, out var token) || token != shortcutToken)
            {
                return;
            }
            CaptureRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private void SyncCaptureGesture()
    {
        if (liveWebView is null || isDisposed || !isAttached)
        {
            return;
        }
        var parts = captureGesture?.Split('+') ?? [];
        var key = parts.LastOrDefault();
        var code = key is null ? null : GetBrowserCode(key);
        var json = code is null ? "null" : JsonSerializer.Serialize(new
        {
            code,
            alt = parts.Contains("Alt"),
            ctrl = parts.Contains("Ctrl"),
            shift = parts.Contains("Shift"),
            meta = parts.Contains("Meta")
        });
        try
        {
            liveWebView.InvokeScript($"window.yohukeSetCaptureGesture && window.yohukeSetCaptureGesture({json});");
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ResetCaptureHost()
    {
        var previous = captureHostLifetime;
        captureHostLifetime = null;
        previous?.Cancel();
        previous?.Dispose();
        if (!isDisposed)
        {
            captureHostLifetime = new();
        }
    }

    private void ReleaseCaptureBridge()
    {
        if (frameCaptureService is not null)
        {
            frameCaptureService.CaptureShortcutRequested -= OnNativeCaptureShortcut;
        }
        captureHostLifetime?.Cancel();
    }

    private static bool TryGetPlayerToken(string? url, out string token)
    {
        token = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }
        foreach (var part in uri.Query.TrimStart('?').Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "token" && Guid.TryParseExact(pair[1], "N", out _))
            {
                token = pair[1];
                return true;
            }
        }
        return false;
    }

    private static string GetBrowserCode(string key)
    {
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z')
        {
            return "Key" + key;
        }
        if (key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1]))
        {
            return "Digit" + key[1];
        }
        if (key.StartsWith("NumPad", StringComparison.Ordinal))
        {
            return "Numpad" + key[6..];
        }
        return key switch
        {
            "Left" => "ArrowLeft", "Right" => "ArrowRight", "Up" => "ArrowUp", "Down" => "ArrowDown",
            "Esc" => "Escape", "Back" or "Backspace" => "Backspace", "Return" => "Enter",
            "OemPlus" => "Equal", "OemMinus" => "Minus", "OemComma" => "Comma", "OemPeriod" => "Period",
            "OemQuestion" => "Slash", "OemSemicolon" => "Semicolon", "OemQuotes" => "Quote",
            "OemOpenBrackets" => "BracketLeft", "OemCloseBrackets" => "BracketRight",
            "OemBackslash" or "OemPipe" => "Backslash", "OemTilde" => "Backquote",
            "Add" => "NumpadAdd", "Subtract" => "NumpadSubtract", "Multiply" => "NumpadMultiply",
            "Divide" => "NumpadDivide", "Decimal" => "NumpadDecimal", _ => key
        };
    }
}
