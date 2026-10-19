using System.Globalization;
using System.Net;
using System.Text.Json;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Captures;

namespace Yohuke.DanmuNeo.Services;

public partial class LivePlayerService
{
    private const int MAXIMUM_PENDING_CAPTURES = 4;
    private readonly Dictionary<string, LiveFrameCaptureRequest> captureRequests = new(StringComparer.Ordinal);
    private readonly TimeSpan captureTimeout;

    /// <summary>创建播放器服务，并可为离线测试指定抓图响应期限。</summary>
    public LivePlayerService(TimeSpan? captureTimeout = null)
    {
        this.captureTimeout = captureTimeout ?? TimeSpan.FromSeconds(10);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(this.captureTimeout, TimeSpan.Zero);
    }

    /// <inheritdoc/>
    public event EventHandler<LiveCaptureShortcutEventArgs>? CaptureShortcutRequested;

    /// <inheritdoc/>
    public async Task<LiveCapturedFrame> RequestFrameAsync(string token, Func<string, Task> invokeScript,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invokeScript);
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N");
        LiveFrameCaptureRequest request;
        CancellationTokenSource lifetime;
        using var deadline = new CancellationTokenSource(captureTimeout);
        lock (gate)
        {
            if (!streams.TryGetValue(token, out var stream))
            {
                throw new OperationCanceledException(Localization.Get("Captures.SessionChanged"));
            }
            if (captureRequests.Count >= MAXIMUM_PENDING_CAPTURES)
            {
                throw new InvalidOperationException(Localization.Get("Captures.Busy"));
            }
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
                stream.CancellationTokenSource.Token, deadline.Token);
            request = new() { Token = token, Stream = stream, RequestedAt = DateTimeOffset.UtcNow, CancellationToken = lifetime.Token };
            captureRequests.Add(requestId, request);
        }

        using (lifetime)
        {
            try
            {
                await invokeScript($"window.yohukeCaptureLiveFrame && window.yohukeCaptureLiveFrame({JsonSerializer.Serialize(requestId)});")
                    .WaitAsync(lifetime.Token);
                return await request.Completion.Task.WaitAsync(lifetime.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested && !request.Stream.CancellationTokenSource.IsCancellationRequested)
            {
                throw new TimeoutException(Localization.Get("Captures.Timeout"));
            }
            finally
            {
                lock (gate)
                {
                    captureRequests.Remove(requestId);
                }
            }
        }
    }

    private async Task<bool> HandleCaptureRequestAsync(HttpListenerContext context, string path,
        CancellationToken cancellationToken)
    {
        var isFrame = path.StartsWith("/capture/", StringComparison.Ordinal);
        var isError = path.StartsWith("/capture-error/", StringComparison.Ordinal);
        var isShortcut = path.StartsWith("/capture-shortcut/", StringComparison.Ordinal);
        if (!isFrame && !isError && !isShortcut)
        {
            return false;
        }
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 405;
            return true;
        }
        var origin = $"http://127.0.0.1:{port}";
        if (!string.Equals(context.Request.Headers["Origin"], origin, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 403;
            return true;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != (isShortcut ? 2 : 3) || !Guid.TryParseExact(parts[1], "N", out _))
        {
            context.Response.StatusCode = 404;
            return true;
        }
        var token = parts[1];
        LivePlayerStreamEntry? stream;
        LiveFrameCaptureRequest? request = null;
        lock (gate)
        {
            streams.TryGetValue(token, out stream);
            if (!isShortcut && (stream is null || !captureRequests.TryGetValue(parts[2], out request) ||
                request.Token != token || !ReferenceEquals(request.Stream, stream) || request.IsReceiving))
            {
                request = null;
            }
            if (request is not null)
            {
                request.IsReceiving = true;
            }
        }
        if (stream is null || (!isShortcut && request is null))
        {
            context.Response.StatusCode = 404;
            return true;
        }
        if (isShortcut)
        {
            if (context.Request.ContentLength64 > 0)
            {
                context.Response.StatusCode = 400;
                return true;
            }
            CaptureShortcutRequested?.Invoke(this, new(token));
            context.Response.StatusCode = 204;
            return true;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            request!.CancellationToken);
        lifetime.CancelAfter(captureTimeout);
        try
        {
            if (isError)
            {
                var bytes = await ReadCaptureBodyAsync(context.Request, 128, lifetime.Token);
                var code = JsonSerializer.Deserialize<string>(bytes);
                var key = code switch
                {
                    "noFrame" => "Captures.NoFrame",
                    "tooLarge" => "Captures.ImageTooLarge",
                    "unsupported" => "Captures.Unsupported",
                    _ => "Captures.Failed"
                };
                CompleteCapture(parts[2], request!, null, new InvalidOperationException(Localization.Get(key)));
            }
            else
            {
                if (!string.Equals(context.Request.ContentType, "image/png", StringComparison.OrdinalIgnoreCase) ||
                    !long.TryParse(context.Request.Headers["X-Capture-Time"], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var milliseconds))
                {
                    context.Response.StatusCode = 400;
                    return true;
                }
                var capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
                if (capturedAt < request!.RequestedAt.AddSeconds(-1) || capturedAt > DateTimeOffset.UtcNow.AddSeconds(1))
                {
                    context.Response.StatusCode = 400;
                    return true;
                }
                var png = await ReadCaptureBodyAsync(context.Request, LiveCapturePngValidator.MAXIMUM_BYTES,
                    lifetime.Token);
                var size = LiveCapturePngValidator.Validate(png);
                DateTimeOffset? startedAt = stream.LiveTime > 0 && stream.LiveTime <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
                    ? DateTimeOffset.FromUnixTimeSeconds(stream.LiveTime) : null;
                CompleteCapture(parts[2], request!, new()
                {
                    PngBytes = png,
                    RoomId = stream.RoomId,
                    BroadcastSessionId = stream.BroadcastSessionId,
                    StartedAt = startedAt?.ToOffset(TimeZoneInfo.Local.GetUtcOffset(startedAt.Value)),
                    CapturedAt = capturedAt.ToOffset(TimeZoneInfo.Local.GetUtcOffset(capturedAt)),
                    Width = size.Width,
                    Height = size.Height
                }, null);
            }
            context.Response.StatusCode = 204;
        }
        catch (InvalidDataException exception)
        {
            CompleteCapture(parts[2], request!, null, exception);
            context.Response.StatusCode = 400;
        }
        catch (JsonException)
        {
            context.Response.StatusCode = 400;
        }
        catch (ArgumentOutOfRangeException)
        {
            context.Response.StatusCode = 400;
        }
        return true;
    }

    private void CompleteCapture(string requestId, LiveFrameCaptureRequest request, LiveCapturedFrame? frame,
        Exception? error)
    {
        lock (gate)
        {
            if (!streams.TryGetValue(request.Token, out var stream) || !ReferenceEquals(stream, request.Stream) ||
                !captureRequests.Remove(requestId))
            {
                return;
            }
            if (error is not null)
            {
                request.Completion.TrySetException(error);
            }
            else
            {
                request.Completion.TrySetResult(frame!);
            }
        }
    }

    private static async Task<byte[]> ReadCaptureBodyAsync(HttpListenerRequest request, int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength64 > maximumBytes)
        {
            throw new InvalidDataException(Localization.Get("Captures.ImageTooLarge"));
        }
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var count = await request.InputStream.ReadAsync(chunk.AsMemory(0,
                Math.Min(chunk.Length, maximumBytes + 1 - (int)buffer.Length)), cancellationToken);
            if (count == 0)
            {
                return buffer.ToArray();
            }
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
            if (buffer.Length > maximumBytes)
            {
                throw new InvalidDataException(Localization.Get("Captures.ImageTooLarge"));
            }
        }
    }

    private static string CreateCaptureScript()
    {
        return """
            let captureGesture = null;
            const captureControllers = new Set();
            window.yohukeSetCaptureGesture = gesture => { captureGesture = gesture; };
            window.yohukeCaptureLiveFrame = async requestId => {
              if (!/^[a-f0-9]{32}$/.test(requestId) || !token) return;
              const controller = new AbortController();
              captureControllers.add(controller);
              const timeout = setTimeout(() => controller.abort(), 8000);
              const options = { method: "POST", signal: controller.signal, credentials: "same-origin" };
              const fail = code => fetch(`/capture-error/${encodeURIComponent(token)}/${requestId}`, {
                ...options, headers: { "Content-Type": "application/json" }, body: JSON.stringify(code)
              }).catch(() => {});
              let canvas = null;
              try {
                if (video.readyState < 2 || !video.videoWidth || !video.videoHeight || video.ended || video.error) {
                  await fail("noFrame"); return;
                }
                if (video.videoWidth > 8192 || video.videoHeight > 8192 || video.videoWidth * video.videoHeight > 34000000) {
                  await fail("tooLarge"); return;
                }
                canvas = document.createElement("canvas");
                canvas.width = video.videoWidth;
                canvas.height = video.videoHeight;
                const context = canvas.getContext("2d");
                if (!context || typeof canvas.toBlob !== "function") { await fail("unsupported"); return; }
                const capturedAt = Date.now();
                context.drawImage(video, 0, 0);
                const blob = await new Promise((resolve, reject) => {
                  const aborted = () => reject(new DOMException("Capture canceled", "AbortError"));
                  if (controller.signal.aborted) { aborted(); return; }
                  controller.signal.addEventListener("abort", aborted, { once: true });
                  canvas.toBlob(value => {
                    controller.signal.removeEventListener("abort", aborted);
                    resolve(value);
                  }, "image/png");
                });
                if (!blob) { await fail("failed"); return; }
                if (blob.size > 33554432) { await fail("tooLarge"); return; }
                await fetch(`/capture/${encodeURIComponent(token)}/${requestId}`, {
                  ...options, headers: { "Content-Type": "image/png", "X-Capture-Time": String(capturedAt) }, body: blob
                });
              } catch (error) {
                if (error && error.name !== "AbortError") await fail("failed");
              } finally {
                clearTimeout(timeout);
                captureControllers.delete(controller);
                if (canvas) { canvas.width = 0; canvas.height = 0; }
              }
            };
            document.addEventListener("keydown", event => {
              const gesture = captureGesture;
              if (!gesture || event.repeat || event.isComposing || event.defaultPrevented ||
                  event.code !== gesture.code || event.altKey !== gesture.alt || event.ctrlKey !== gesture.ctrl ||
                  event.shiftKey !== gesture.shift || event.metaKey !== gesture.meta) return;
              event.preventDefault();
              event.stopPropagation();
              fetch(`/capture-shortcut/${encodeURIComponent(token)}`, { method: "POST", credentials: "same-origin" }).catch(() => {});
            }, true);
            window.addEventListener("beforeunload", () => {
              captureGesture = null;
              for (const controller of captureControllers) controller.abort();
              captureControllers.clear();
            });
            """;
    }
}
