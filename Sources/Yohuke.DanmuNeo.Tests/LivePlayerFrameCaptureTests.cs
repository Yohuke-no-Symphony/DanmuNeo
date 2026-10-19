using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>通过离线回环请求验证原帧抓取协议及会话隔离。</summary>
public sealed class LivePlayerFrameCaptureTests
{
    /// <summary>原帧沿用已注册会话的房间、开播时间和实际抓图时间。</summary>
    [Fact]
    public async Task OriginalFrameUsesSessionRoomStartTimeAndActualCaptureTime()
    {
        using var service = new LivePlayerService();
        using var client = new HttpClient();
        var session = await service.CreatePlayerAsync(new() { RoomId = "offline", LiveTime = 1728000000, StreamUrl = "https://example.invalid/offline" }, "");
        var now = DateTimeOffset.UtcNow;
        var captured = await service.RequestFrameAsync(session.Token, async script =>
        {
            var id = GetRequestId(script);
            using var request = CreateRequest(session.PlayerUrl, session.Token, id, LiveCaptureStoreTests.CreateFrame().PngBytes, now);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            using var duplicate = CreateRequest(session.PlayerUrl, session.Token, id, LiveCaptureStoreTests.CreateFrame().PngBytes, now);
            using var rejected = await client.SendAsync(duplicate);
            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        });
        Assert.Equal("offline", captured.RoomId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1728000000), captured.StartedAt);
        Assert.Equal(now.ToUnixTimeMilliseconds(), captured.CapturedAt.ToUnixTimeMilliseconds());
        Assert.Equal(1, captured.Width);
        Assert.Equal(1, captured.Height);
        Assert.Equal(1728000000, session.LiveTime);
    }

    /// <summary>外部来源和未知请求不能完成原帧抓取。</summary>
    [Fact]
    public async Task ForeignOriginAndUnknownRequestCannotCompleteCapture()
    {
        using var service = new LivePlayerService(captureTimeout: TimeSpan.FromMilliseconds(150));
        using var client = new HttpClient();
        var session = await service.CreatePlayerAsync(new() { RoomId = "offline", StreamUrl = "https://example.invalid/offline" }, "");
        await Assert.ThrowsAsync<TimeoutException>(() => service.RequestFrameAsync(session.Token, async script =>
        {
            using var request = CreateRequest(session.PlayerUrl, session.Token, GetRequestId(script), LiveCaptureStoreTests.CreateFrame().PngBytes, DateTimeOffset.UtcNow);
            request.Headers.Remove("Origin");
            request.Headers.Add("Origin", "https://example.invalid");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var unknown = CreateRequest(session.PlayerUrl, session.Token, Guid.NewGuid().ToString("N"), LiveCaptureStoreTests.CreateFrame().PngBytes, DateTimeOffset.UtcNow);
            using var missing = await client.SendAsync(unknown);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }));
    }

    /// <summary>撤销播放会话后拒绝迟到原帧。</summary>
    [Fact]
    public async Task RevokeAndCancellationRejectLateFrames()
    {
        using var service = new LivePlayerService();
        using var client = new HttpClient();
        var session = await service.CreatePlayerAsync(new() { RoomId = "old", StreamUrl = "https://example.invalid/offline" }, "");
        string? requestId = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RequestFrameAsync(session.Token, script =>
        {
            requestId = GetRequestId(script);
            service.Revoke(session.Token);
            return Task.CompletedTask;
        }));
        using var late = CreateRequest(session.PlayerUrl, session.Token, requestId!, LiveCaptureStoreTests.CreateFrame().PngBytes, DateTimeOffset.UtcNow);
        using var response = await client.SendAsync(late);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>非法 PNG 和过大尺寸可解释失败并拒绝进入图库。</summary>
    [Fact]
    public async Task InvalidPngAndDimensionsFailWithoutAcceptingTheFrame()
    {
        using var service = new LivePlayerService();
        using var client = new HttpClient();
        var session = await service.CreatePlayerAsync(new() { RoomId = "offline", StreamUrl = "https://example.invalid/offline" }, "");
        var tooWide = LiveCaptureStoreTests.CreateFrame().PngBytes.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(tooWide.AsSpan(16, 4), 8193);
        foreach (var bytes in new[] { new byte[] { 1, 2, 3 }, tooWide })
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.RequestFrameAsync(session.Token, async script =>
            {
                using var request = CreateRequest(session.PlayerUrl, session.Token, GetRequestId(script), bytes, DateTimeOffset.UtcNow);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }));
        }
    }

    /// <summary>无画面错误可见且未知开播时间保持未知。</summary>
    [Fact]
    public async Task NoFrameFailureIsVisibleAndUnknownBroadcastStartStaysUnknown()
    {
        using var service = new LivePlayerService();
        using var client = new HttpClient();
        var session = await service.CreatePlayerAsync(new() { RoomId = "offline", StreamUrl = "https://example.invalid/offline" }, "");
        var origin = new Uri(session.PlayerUrl).GetLeftPart(UriPartial.Authority);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestFrameAsync(session.Token, async script =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, origin + "/capture-error/" + session.Token + "/" + GetRequestId(script))
            {
                Content = new StringContent("\"noFrame\"", Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Origin", origin);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }));
        Assert.Equal(Yohuke.DanmuNeo.I18n.Localization.Get("Captures.NoFrame"), error.Message);
        var frame = await service.RequestFrameAsync(session.Token, async script =>
        {
            using var request = CreateRequest(session.PlayerUrl, session.Token, GetRequestId(script), LiveCaptureStoreTests.CreateFrame().PngBytes, DateTimeOffset.UtcNow);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        });
        Assert.Null(frame.StartedAt);
        Assert.Contains(":unknown:", frame.BroadcastSessionId);
    }

    /// <summary>原生焦点快捷动作只接受同源的有效会话。</summary>
    [Fact]
    public async Task NativeShortcutRequiresCurrentSessionAndSameOrigin()
    {
        using var service = new LivePlayerService();
        using var client = new HttpClient();
        var session = await service.CreatePlayerAsync(new() { RoomId = "offline", StreamUrl = "https://example.invalid/offline" }, "");
        var origin = new Uri(session.PlayerUrl).GetLeftPart(UriPartial.Authority);
        var tokens = new List<string>();
        service.CaptureShortcutRequested += (_, args) => tokens.Add(args.Token);
        using var missingOrigin = await client.PostAsync(origin + "/capture-shortcut/" + session.Token, null);
        Assert.Equal(HttpStatusCode.Forbidden, missingOrigin.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, origin + "/capture-shortcut/" + session.Token);
        request.Headers.Add("Origin", origin);
        using var accepted = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(new[] { session.Token }, tokens);
        service.Revoke(session.Token);
        using var stale = new HttpRequestMessage(HttpMethod.Post, origin + "/capture-shortcut/" + session.Token);
        stale.Headers.Add("Origin", origin);
        using var rejected = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        Assert.Single(tokens);
    }

    /// <summary>调用方取消及服务释放均结束待完成抓图。</summary>
    [Fact]
    public async Task CallerCancellationAndServiceDisposalReleasePendingCapture()
    {
        using var service = new LivePlayerService();
        var session = await service.CreatePlayerAsync(new() { RoomId = "offline", StreamUrl = "https://example.invalid/offline" }, "");
        using var canceled = new System.Threading.CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RequestFrameAsync(session.Token, _ =>
        {
            canceled.Cancel();
            return Task.CompletedTask;
        }, canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RequestFrameAsync(session.Token, _ =>
        {
            service.Dispose();
            return Task.CompletedTask;
        }));
    }

    /// <summary>页面使用自然视频尺寸并提供受限 PNG 和快捷键桥接。</summary>
    [Fact]
    public void PageCapturesNaturalVideoFrameAndPostsBoundedPngWithShortcutBridge()
    {
        var html = (string)typeof(LivePlayerService).GetMethod("CreatePlayerHtml", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
        Assert.Contains("window.yohukeCaptureLiveFrame", html);
        Assert.Contains("canvas.width = video.videoWidth", html);
        Assert.Contains("canvas.height = video.videoHeight", html);
        Assert.Contains("context.drawImage(video, 0, 0)", html);
        Assert.Contains("canvas.toBlob", html);
        Assert.Contains("X-Capture-Time", html);
        Assert.Contains("window.yohukeSetCaptureGesture", html);
        Assert.Contains("event.isComposing", html);
        Assert.Contains("event.repeat", html);
        Assert.DoesNotContain("canvas.drawImage(root", html);
    }

    internal static string GetRequestId(string script)
    {
        return Regex.Match(script, "yohukeCaptureLiveFrame\\(\"([a-f0-9]{32})\"\\)").Groups[1].Value;
    }

    internal static HttpRequestMessage CreateRequest(string playerUrl, string token, string requestId, byte[] png, DateTimeOffset now)
    {
        var origin = new Uri(playerUrl).GetLeftPart(UriPartial.Authority);
        var request = new HttpRequestMessage(HttpMethod.Post, origin + "/capture/" + token + "/" + requestId)
        {
            Content = new ByteArrayContent(png)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-Capture-Time", now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return request;
    }
}
