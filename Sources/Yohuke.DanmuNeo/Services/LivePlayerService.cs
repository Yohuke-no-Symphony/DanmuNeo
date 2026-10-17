using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.Logging;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 用本地 HTTP 服务承载直播播放器页面并代理 B 站直播流。
/// </summary>
public class LivePlayerService : ILivePlayerService, IDisposable
{
    private const string MPEGTS_RESOURCE_NAME = "Yohuke.DanmuNeo.Assets.Player.mpegts.js";
    private const int MAXIMUM_DIAGNOSTIC_LENGTH = 4096;
    private const int MAXIMUM_DIAGNOSTIC_SIGNATURES = 64;
    private readonly Dictionary<string, LivePlayerStreamEntry> streams = [];
    private readonly Lock gate = new();

    private readonly HttpClient httpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private HttpListener? listener;
    private CancellationTokenSource? listenerTokenSource;
    private int port;
    private string? mpegtsScript;

    /// <inheritdoc/>
    public Task<LivePlayerSession> CreatePlayerAsync(
        LiveStreamPlaySource source,
        string? cookie,
        CancellationToken cancellationToken = default)
    {
        EnsureStarted();

        var token = Guid.NewGuid().ToString("N");
        var entry = new LivePlayerStreamEntry
        {
            StreamUrl = source.StreamUrl,
            RoomId = source.RoomId,
            Cookie = ApiCookieHelper.CreateBilibiliCookie(cookie ?? "")
        };

        lock (gate)
        {
            streams[token] = entry;
        }

        var playerUrl =
            $"http://127.0.0.1:{port}/player?token={Uri.EscapeDataString(token)}&v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        return Task.FromResult(new LivePlayerSession
        {
            Token = token,
            PlayerUrl = playerUrl
        });
    }

    /// <inheritdoc/>
    public void Revoke(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        LivePlayerStreamEntry? entry;

        lock (gate)
        {
            if (!streams.Remove(token, out entry))
            {
                return;
            }
        }

        entry.CancellationTokenSource.Cancel();
        entry.CancellationTokenSource.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        listenerTokenSource?.Cancel();
        listener?.Close();
        httpClient.Dispose();

        lock (gate)
        {
            foreach (var entry in streams.Values)
            {
                entry.CancellationTokenSource.Cancel();
                entry.CancellationTokenSource.Dispose();
            }

            streams.Clear();
        }

        listenerTokenSource?.Dispose();
    }

    private void EnsureStarted()
    {
        if (listener is not null)
        {
            return;
        }

        port = GetFreePort();
        listenerTokenSource = new();
        listener = new();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        _ = Task.Run(() => ListenAsync(listenerTokenSource.Token));
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && listener is not null && listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync();
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                StartupLog.Append("播放器 HTTP 服务监听失败", AppLogLevel.ERROR, "播放器", exception);
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "";

            if (path == "/player")
            {
                await WriteTextAsync(context.Response, CreatePlayerHtml(), "text/html; charset=utf-8",
                    cancellationToken);
                return;
            }

            if (path == "/mpegts.js")
            {
                await WriteTextAsync(context.Response, GetMpegtsScript(), "application/javascript; charset=utf-8",
                    cancellationToken);
                return;
            }

            if (path.StartsWith("/diagnostic/", StringComparison.Ordinal))
            {
                await ReceiveDiagnosticAsync(context, path["/diagnostic/".Length..], cancellationToken);
                return;
            }

            if (path.StartsWith("/stream/", StringComparison.Ordinal))
            {
                await ProxyStreamAsync(context, path["/stream/".Length..], cancellationToken);
                return;
            }

            context.Response.StatusCode = 404;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            var path = context.Request.Url?.AbsolutePath ?? "";
            var active = !path.StartsWith("/stream/", StringComparison.Ordinal) ||
                         FindStream(path["/stream/".Length..]) is not null;

            if (!cancellationToken.IsCancellationRequested && active)
            {
                StartupLog.Append("播放器请求失败", AppLogLevel.ERROR, "播放器", exception);
            }

            if (context.Response.OutputStream.CanWrite)
            {
                context.Response.StatusCode = 500;
                await WriteTextAsync(context.Response, "播放器服务错误", "text/plain; charset=utf-8", cancellationToken);
                return;
            }
        }
        finally
        {
            TryClose(context.Response);
        }
    }

    private LivePlayerStreamEntry? FindStream(string rawToken)
    {
        lock (gate)
        {
            return streams.GetValueOrDefault(Uri.UnescapeDataString(rawToken));
        }
    }

    private async Task ReceiveDiagnosticAsync(HttpListenerContext context, string rawToken,
        CancellationToken cancellationToken)
    {
        if (context.Request.HttpMethod != "POST")
        {
            context.Response.StatusCode = 405;
            return;
        }

        if (context.Request.Headers["Origin"] is { } origin && origin != $"http://127.0.0.1:{port}")
        {
            context.Response.StatusCode = 403;
            return;
        }

        var entry = FindStream(rawToken);

        if (entry is null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var buffer = new char[MAXIMUM_DIAGNOSTIC_LENGTH + 1];
        var count = 0;

        while (count < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(count), cancellationToken);

            if (read == 0)
            {
                break;
            }

            count += read;
        }

        if (count > MAXIMUM_DIAGNOSTIC_LENGTH)
        {
            context.Response.StatusCode = 413;
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(new string(buffer, 0, count));
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out var kind) ||
                kind.ValueKind != JsonValueKind.String || !root.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.String)
            {
                context.Response.StatusCode = 400;
                return;
            }

            var diagnostic = kind.GetString();

            if (diagnostic is not ("error" or "warning" or "recovered"))
            {
                context.Response.StatusCode = 400;
                return;
            }

            if (diagnostic != "recovered" && string.IsNullOrWhiteSpace(message.GetString()))
            {
                context.Response.StatusCode = 400;
                return;
            }

            var networkFailure = root.TryGetProperty("category", out var category) &&
                                 category.ValueKind == JsonValueKind.String && category.GetString() == "network";
            ReportDiagnostic(entry, diagnostic == "recovered" ? null : message.GetString(),
                diagnostic == "warning" ? AppLogLevel.WARNING : AppLogLevel.ERROR,
                networkFailure: networkFailure);
            context.Response.StatusCode = 204;
        }
        catch (JsonException)
        {
            context.Response.StatusCode = 400;
        }
    }

    private void ReportDiagnostic(LivePlayerStreamEntry entry, string? message, AppLogLevel level,
        bool upstreamFailure = false, bool networkFailure = false)
    {
        var safeMessage = message is null ? null : AppLogSanitizer.Sanitize(message);

        lock (gate)
        {
            if (!streams.Values.Contains(entry) || networkFailure && entry.HasUpstreamFailure)
            {
                return;
            }

            if (safeMessage is null)
            {
                if (entry.ReportedDiagnostics.Count == 0)
                {
                    return;
                }

                entry.ReportedDiagnostics.Clear();
                entry.DiagnosticOrder.Clear();
                entry.HasUpstreamFailure = false;
            }
            else
            {
                entry.HasUpstreamFailure |= upstreamFailure;
                var signature = $"{level}|{safeMessage}";

                if (!entry.ReportedDiagnostics.Add(signature))
                {
                    return;
                }

                entry.DiagnosticOrder.Enqueue(signature);

                while (entry.DiagnosticOrder.Count > MAXIMUM_DIAGNOSTIC_SIGNATURES)
                {
                    entry.ReportedDiagnostics.Remove(entry.DiagnosticOrder.Dequeue());
                }
            }
        }

        StartupLog.Append(safeMessage ?? "直播播放已恢复", safeMessage is null ? AppLogLevel.INFO : level,
            $"播放器/房间 {entry.RoomId}");
    }

    private async Task ProxyStreamAsync(
        HttpListenerContext context,
        string rawToken,
        CancellationToken serviceCancellationToken)
    {
        var token = Uri.UnescapeDataString(rawToken);
        LivePlayerStreamEntry? entry;

        lock (gate)
        {
            streams.TryGetValue(token, out entry);
        }

        if (entry is null)
        {
            context.Response.StatusCode = 404;
            await WriteTextAsync(context.Response, "直播流已失效", "text/plain; charset=utf-8", serviceCancellationToken);
            return;
        }

        using var linkedTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(serviceCancellationToken,
                entry.CancellationTokenSource.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, entry.StreamUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", BaseApi.DEFAULT_USER_AGENT);
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.TryAddWithoutValidation("Origin", "https://live.bilibili.com");
        request.Headers.TryAddWithoutValidation("Referer", $"https://live.bilibili.com/{entry.RoomId}");

        if (!string.IsNullOrWhiteSpace(entry.Cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", entry.Cookie);
        }

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            linkedTokenSource.Token);
        StartupLog.Append(
            $"Live player upstream roomId={entry.RoomId} status={(int)response.StatusCode} contentType={response.Content.Headers.ContentType} host={request.RequestUri?.Host}");
        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "video/x-flv";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Cache-Control"] = "no-store";

        if (!response.IsSuccessStatusCode)
        {
            var errorText = $"直播流请求失败：{(int)response.StatusCode} {response.ReasonPhrase}";
            ReportDiagnostic(entry, errorText, AppLogLevel.ERROR, upstreamFailure: true);
            await WriteTextAsync(context.Response, errorText, "text/plain; charset=utf-8", linkedTokenSource.Token);
            return;
        }

        lock (gate)
        {
            entry.HasUpstreamFailure = false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(linkedTokenSource.Token);
        await stream.CopyToAsync(context.Response.OutputStream, linkedTokenSource.Token);
    }

    private static async Task WriteTextAsync(
        HttpListenerResponse response,
        string text,
        string contentType,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    private string GetMpegtsScript()
    {
        if (mpegtsScript is not null)
        {
            return mpegtsScript;
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(MPEGTS_RESOURCE_NAME) ??
                           throw new InvalidOperationException("播放器脚本资源缺失");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        mpegtsScript = reader.ReadToEnd();
        return mpegtsScript;
    }

    private static string CreatePlayerHtml()
    {
        return """
               <!doctype html>
               <html lang="zh-CN">
               <head>
                 <meta charset="utf-8">
                 <meta name="viewport" content="width=device-width, initial-scale=1">
                 <style>
                   html, body { width: 100%; height: 100%; margin: 0; background: #05080c; color: #d8dee9; font-family: sans-serif; overflow: hidden; }
                   #root { position: fixed; inset: 0; display: grid; }
                   video { width: 100%; height: 100%; object-fit: contain; background: #05080c; }
                   #status { position: absolute; left: 12px; bottom: 10px; padding: 6px 8px; border-radius: 6px; background: rgba(5, 8, 12, 0.72); font-size: 12px; }
                   #soundGate { position: absolute; right: 12px; top: 10px; padding: 7px 10px; border: 1px solid rgba(216, 222, 233, 0.28); border-radius: 6px; background: rgba(5, 8, 12, 0.78); color: #d8dee9; font-size: 12px; cursor: pointer; }
                   #soundGate.hidden { display: none; }
                   video::-webkit-media-controls-timeline { display: none; }
                   video::-webkit-media-controls-current-time-display { display: none; }
                   video::-webkit-media-controls-time-remaining-display { display: none; }
                   video::-webkit-media-controls-seek-back-button { display: none; }
                   video::-webkit-media-controls-seek-forward-button { display: none; }
                 </style>
               </head>
               <body>
                 <div id="root">
                   <video id="player" autoplay muted playsinline></video>
                   <div id="status">正在连接直播流...</div>
                   <button id="soundGate" type="button">点击开启声音</button>
                 </div>
                 <script src="/mpegts.js"></script>
                 <script>
                   const params = new URLSearchParams(location.search);
                   const token = params.get("token") || "";
                   const root = document.getElementById("root");
                   const status = document.getElementById("status");
                   const soundGate = document.getElementById("soundGate");
                   const video = document.getElementById("player");
                   let player = null;
                   let retryCount = 0;
                   let recoveryCount = 0;
                   let watchdogTimer = null;
                       let recoveryTimer = null;
                       let isRecovering = false;
                       let soundUnlocked = false;
                       let requestedVolume = 1;
                       let requestedMuted = false;
                       let lastActiveAt = Date.now();
                       let lastTimelineActiveAt = Date.now();
                       let lastVideoFrameAt = Date.now();
                       let lastCurrentTime = 0;
                       let lastDecodedFrames = 0;
                       let lastVideoSizeText = "";
                       let lastMediaInfo = null;
                       let lastStatisticsInfo = null;
                       let hasVideoOutput = false;
                       let canTrackDecodedFrames = false;
                       let stuckSince = 0;
                       const maxRetries = 5;
                       const maxRecoveries = 5;
                       const waitingTimeoutMs = 8000;
                       const noVideoTimeoutMs = 8000;
                       const videoFrameTimeoutMs = 10000;
                       const stuckTimeoutMs = 10000;
                       const edgeRemainSeconds = 1.2;

                   function setStatus(text) {
                     status.textContent = text;
                   }

                   function reportDiagnostic(kind, message, category = "playback") {
                     if (!token) {
                       return;
                     }
                     fetch(`/diagnostic/${encodeURIComponent(token)}`, {
                       method: "POST",
                       headers: { "Content-Type": "application/json" },
                       body: JSON.stringify({ kind, category, message: String(message || "").slice(0, 2000) })
                     }).catch(() => {});
                   }

                   window.addEventListener("error", event => {
                     reportDiagnostic("error", event.error && event.error.stack || event.message || "播放器脚本错误");
                   });
                   window.addEventListener("unhandledrejection", event => {
                     const error = event.reason;
                     if (!error || error.name !== "AbortError") {
                       reportDiagnostic("error", error && (error.stack || error.message) || "播放器异步操作失败");
                     }
                   });

                   function setSoundGateVisible(isVisible) {
                     soundGate.classList.toggle("hidden", !isVisible);
                   }

                   function applyAudioState() {
                     video.volume = requestedVolume;
                     video.muted = requestedMuted || !soundUnlocked;
                     setSoundGateVisible(!requestedMuted && !soundUnlocked);
                   }

                   function getPlaybackStatusText() {
                     const message = recoveryCount > 0 ? "直播已恢复" : "播放中";

                     if (video.muted) {
                       return requestedMuted ? `${message}（已静音）` : `${message}（点击开启声音）`;
                     }

                     return message;
                   }

                   function setPlaybackStatus() {
                     setStatus(getPlaybackStatusText());
                   }

                   function handlePlayFailure(error) {
                     const message = error && error.message ? error.message : "";

                     if (error && error.name === "NotAllowedError") {
                       setStatus(`点击播放器开始播放${message ? `：${message}` : ""}`);
                       setSoundGateVisible(!requestedMuted);
                       return;
                     }

                     setStatus(`播放启动失败${message ? `：${message}` : ""}`);
                     if (!error || error.name !== "AbortError") {
                       reportDiagnostic("error", `播放启动失败：${message}`);
                     }
                   }

                   function playVideo() {
                     const promise = video.play();

                     if (promise) {
                       promise.then(setPlaybackStatus).catch(handlePlayFailure);
                     }
                   }

                   function unlockSoundByUserGesture() {
                     if (!soundUnlocked) {
                       soundUnlocked = true;
                       applyAudioState();
                       setPlaybackStatus();
                     }

                     playVideo();
                   }

                   window.yohukeSetLivePlayerAudio = (volume, isMuted) => {
                     const normalizedVolume = Number(volume);
                     requestedVolume = Number.isFinite(normalizedVolume)
                       ? Math.min(1, Math.max(0, normalizedVolume))
                       : 1;
                     requestedMuted = !!isMuted;
                     applyAudioState();

                     if (!video.paused && !video.ended) {
                       setPlaybackStatus();
                     }
                   };

                   function destroyPlayer() {
                     if (watchdogTimer) {
                       clearInterval(watchdogTimer);
                       watchdogTimer = null;
                     }

                     if (recoveryTimer) {
                       clearTimeout(recoveryTimer);
                       recoveryTimer = null;
                     }

                     isRecovering = false;

                     if (!player) {
                       return;
                     }

                     try {
                       player.unload();
                       player.detachMediaElement();
                       player.destroy();
                     } catch {
                     }

                     player = null;
                   }

                       function readVideoSize() {
                         const width = Number(video.videoWidth || (lastMediaInfo && lastMediaInfo.width) || 0);
                         const height = Number(video.videoHeight || (lastMediaInfo && lastMediaInfo.height) || 0);
    
                         return {
                           width: Number.isFinite(width) ? width : 0,
                           height: Number.isFinite(height) ? height : 0
                         };
                       }
    
                       function readDecodedFrames() {
                         if (video.getVideoPlaybackQuality) {
                           const quality = video.getVideoPlaybackQuality();
                           const frames = Number(quality && quality.totalVideoFrames);
    
                           if (Number.isFinite(frames)) {
                             canTrackDecodedFrames = true;
                             return frames;
                           }
                         }
    
                         const webkitFrames = Number(video.webkitDecodedFrameCount);
    
                         if (Number.isFinite(webkitFrames)) {
                           canTrackDecodedFrames = true;
                           return webkitFrames;
                         }
    
                         const statisticsFrames = Number(lastStatisticsInfo && lastStatisticsInfo.decodedFrames);
    
                         if (Number.isFinite(statisticsFrames)) {
                           canTrackDecodedFrames = true;
                           return statisticsFrames;
                         }
    
                         return lastDecodedFrames;
                       }
    
                       function markTimelineActive() {
                         const now = Date.now();
                         lastActiveAt = now;
                         lastTimelineActiveAt = now;
                       }
    
                       function markNetworkActive() {
                         lastActiveAt = Date.now();
                       }
    
                       function markVideoFrameActive() {
                         const now = Date.now();
                         lastActiveAt = now;
                         lastVideoFrameAt = now;
                         stuckSince = 0;
                       }
    
                       function updateVideoMetrics() {
                         const size = readVideoSize();
                         let hasFreshFrame = false;
    
                         if (size.width > 0 && size.height > 0) {
                           lastVideoSizeText = `${Math.round(size.width)}x${Math.round(size.height)}`;
    
                           if (!hasVideoOutput) {
                             hasVideoOutput = true;
                             hasFreshFrame = true;
                           }
                         }
    
                         const decodedFrames = readDecodedFrames();
    
                         if (decodedFrames > lastDecodedFrames) {
                           lastDecodedFrames = decodedFrames;
                           hasVideoOutput = true;
                           hasFreshFrame = true;
                         } else if (decodedFrames < lastDecodedFrames) {
                           lastDecodedFrames = decodedFrames;
                         }
    
                         if (hasFreshFrame) {
                           markVideoFrameActive();
                         }
    
                         return hasVideoOutput;
                       }
    
                       function resetVideoDiagnostics() {
                         const now = Date.now();
                         lastActiveAt = now;
                         lastTimelineActiveAt = now;
                         lastVideoFrameAt = now;
                         lastCurrentTime = video.currentTime || 0;
                         lastDecodedFrames = 0;
                         lastVideoSizeText = "";
                         lastMediaInfo = null;
                         lastStatisticsInfo = null;
                         hasVideoOutput = false;
                         canTrackDecodedFrames = false;
                         stuckSince = 0;
                       }

                       function seekToLiveEdge(reason) {
                         const buffered = video.buffered;

                     if (!buffered || buffered.length <= 0) {
                       return false;
                     }

                     const edge = buffered.end(buffered.length - 1);
                     const target = Math.max(0, edge - edgeRemainSeconds);

                     if (!Number.isFinite(target) || Math.abs(video.currentTime - target) < 0.2) {
                       return false;
                     }

                     video.currentTime = target;
                         setStatus(`${reason}，正在跳到最新缓冲...`);
                         return true;
                       }

                   function recoverFromStuck(reason) {
                     if (isRecovering) {
                       return;
                     }

                     isRecovering = true;

                     if (recoveryCount >= maxRecoveries) {
                       reportDiagnostic("error", `${reason}，已停止自动恢复`);
                       setStatus(`${reason}，已停止自动恢复，请点击追帧。`);
                       destroyPlayer();
                       return;
                     }

                     recoveryCount++;
                     reportDiagnostic("warning", reason);

                         if (seekToLiveEdge(reason)) {
                           lastActiveAt = Date.now();
                           lastTimelineActiveAt = lastActiveAt;
                           lastVideoFrameAt = lastActiveAt;
                           lastCurrentTime = video.currentTime || 0;
                           lastDecodedFrames = readDecodedFrames();
                           stuckSince = 0;
                           isRecovering = false;
                           return;
                     }

                     retryCount++;
                     setStatus(`${reason}，正在重建直播流（${recoveryCount}/${maxRecoveries}）...`);
                     recoveryTimer = setTimeout(() => {
                       recoveryTimer = null;
                       createPlayer();
                     }, 500);
                   }

                   function startWatchdog() {
                     if (watchdogTimer) {
                       clearInterval(watchdogTimer);
                     }

                         watchdogTimer = setInterval(() => {
                           const now = Date.now();
                           const currentTime = video.currentTime || 0;
                           const isTimeMoving = Math.abs(currentTime - lastCurrentTime) > 0.05;
                           lastCurrentTime = currentTime;
                           updateVideoMetrics();
    
                           if (isTimeMoving) {
                             markTimelineActive();
                           }
    
                           if (video.paused || video.ended) {
                             return;
                           }
    
                           if (!hasVideoOutput &&
                               currentTime > 0 &&
                               now - lastVideoFrameAt > noVideoTimeoutMs) {
                             recoverFromStuck("直播画面未输出");
                             return;
                           }
    
                           if (hasVideoOutput &&
                               canTrackDecodedFrames &&
                               now - lastVideoFrameAt > videoFrameTimeoutMs &&
                               now - lastTimelineActiveAt < waitingTimeoutMs) {
                             recoverFromStuck("直播画面卡住");
                             return;
                           }
    
                           if (isTimeMoving) {
                             return;
                           }
    
                           if (video.readyState < HTMLMediaElement.HAVE_FUTURE_DATA &&
                               now - lastActiveAt > waitingTimeoutMs) {
                         recoverFromStuck("直播缓冲超时");
                         return;
                       }

                       if (!stuckSince) {
                         stuckSince = now;
                       }

                       if (now - stuckSince > stuckTimeoutMs) {
                         recoverFromStuck("直播画面卡住");
                       }
                     }, 1000);
                   }

                   function createPlayer() {
                     if (!token) {
                       setStatus("直播流令牌缺失");
                       return;
                     }

                     if (!window.mpegts || !mpegts.isSupported()) {
                       reportDiagnostic("error", "当前 WebView 不支持 MSE 直播播放");
                       setStatus("当前 WebView 不支持 MSE 直播播放");
                       return;
                         }
    
                         destroyPlayer();
                         resetVideoDiagnostics();
                         applyAudioState();
                         startWatchdog();
                         setStatus(retryCount > 0 ? `正在重新连接直播流（${retryCount}/${maxRetries}）...` : "正在连接直播流...");
                     const streamUrl = new URL(`/stream/${encodeURIComponent(token)}?v=${Date.now()}`, location.href).toString();
                     player = mpegts.createPlayer({
                       type: "flv",
                       isLive: true,
                       url: streamUrl
                     }, {
                       enableWorker: true,
                       enableStashBuffer: false,
                       liveBufferLatencyChasing: true,
                       liveBufferLatencyChasingOnPaused: true,
                       liveSync: true,
                       liveSyncPlaybackRate: 1.4,
                       autoCleanupSourceBuffer: true
                     });
                     player.attachMediaElement(video);
                     player.load();
                     player.on(mpegts.Events.ERROR, (errorType, errorDetail, errorInfo) => {
                       const infoText = typeof errorInfo === "string"
                         ? errorInfo
                         : JSON.stringify(errorInfo || {});
                       const message = `播放中断：${errorType || "Unknown"} / ${errorDetail || "Unknown"} ${infoText}`;
                       setStatus(message);
                       reportDiagnostic("error", message,
                         errorType === mpegts.ErrorTypes.NETWORK_ERROR ? "network" : "media");
                       console.error(message, errorInfo);

                       if (retryCount >= maxRetries) {
                         setStatus(`${message}。已停止自动重连，请点击追帧或播放重试。`);
                         destroyPlayer();
                         return;
                       }

                       if (isRecovering) {
                         return;
                       }

                       isRecovering = true;
                       retryCount++;
                       recoveryTimer = setTimeout(() => {
                         recoveryTimer = null;
                         createPlayer();
                           }, 1500);
                         });
                         player.on(mpegts.Events.MEDIA_INFO, mediaInfo => {
                           lastMediaInfo = mediaInfo || {};
                           updateVideoMetrics();
                           console.info("直播媒体信息", {
                             hasVideo: lastMediaInfo.hasVideo,
                             videoCodec: lastMediaInfo.videoCodec,
                             width: lastMediaInfo.width,
                             height: lastMediaInfo.height,
                             fps: lastMediaInfo.fps
                           });
                         });
                         player.on(mpegts.Events.STATISTICS_INFO, statisticsInfo => {
                           lastStatisticsInfo = statisticsInfo || {};
                           updateVideoMetrics();
                         });
                         video.onplaying = () => {
                           reportDiagnostic("recovered", "");
                           retryCount = 0;
                           isRecovering = false;
                           markTimelineActive();
                           updateVideoMetrics();
                           setPlaybackStatus();
                         };
                         video.onloadedmetadata = () => updateVideoMetrics();
                         video.onresize = () => updateVideoMetrics();
                         video.oncanplay = () => updateVideoMetrics();
                         video.ontimeupdate = () => {
                           markTimelineActive();
                           updateVideoMetrics();
                         };
                         video.onprogress = () => markNetworkActive();
                         video.onwaiting = () => setStatus("缓冲中，正在监测直播状态...");
                     video.onstalled = () => recoverFromStuck("直播流停止响应");
                     video.onerror = () => {
                       const error = video.error;
                       reportDiagnostic("error", error ? `播放器错误：${error.code}` : "播放器错误");
                       setStatus(error ? `播放器错误：${error.code}` : "播放器错误");
                     };
                     playVideo();
                   }

                   window.addEventListener("beforeunload", destroyPlayer);
                   root.addEventListener("click", unlockSoundByUserGesture);
                   root.addEventListener("pointerdown", unlockSoundByUserGesture);
                   root.addEventListener("touchstart", unlockSoundByUserGesture, { passive: true });
                   document.addEventListener("keydown", unlockSoundByUserGesture);
                   createPlayer();
                 </script>
               </body>
               </html>
               """;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void TryClose(HttpListenerResponse response)
    {
        try
        {
            response.Close();
        }
        catch
        {
        }
    }
}
