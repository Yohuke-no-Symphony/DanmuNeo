using System.Net;
using System.Net.Http;
using System.Threading;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证创建记录文件时仅以 B 站时间和开播时间建立参考起点。
/// </summary>
public class BilibiliLiveMarkerClockServiceTests
{
    /// <summary>
    /// 无播放地址仍可取时；补齐两响应之间的耗时，Cookie 只发送给房间请求。
    /// </summary>
    [Fact]
    public async Task CreatesStartTimeWithoutPlayUrlAndAccountsForElapsedBetweenResponses()
    {
        const long SERVER_NOW = 1792454400;
        var timeProvider = new LiveMarkerTestTimeProvider(DateTimeOffset.FromUnixTimeSeconds(SERVER_NOW).AddHours(12));
        var cookies = new Dictionary<string, string>();
        using var handler = new LiveMarkerClockHttpHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            cookies[path] = request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : "";

            if (path == "/x/click-interface/click/now")
            {
                Assert.Equal("api.bilibili.com", request.RequestUri.Host);
                return Task.FromResult(Json("{\"code\":0,\"data\":{\"now\":" + SERVER_NOW + "}}"));
            }

            Assert.Equal("api.live.bilibili.com", request.RequestUri.Host);
            Assert.Contains("room_id=100", request.RequestUri.Query);
            timeProvider.Advance(TimeSpan.FromMilliseconds(5250));
            return Task.FromResult(Json("{\"code\":0,\"data\":{\"live_status\":1,\"live_time\":" + (SERVER_NOW - 3600) + ",\"playurl_info\":null}}"));
        });
        using var client = new HttpClient(handler);
        var service = new BilibiliLiveMarkerClockService(client, timeProvider);

        var startTime = await service.CreateStartTimeAsync("100", "SESSDATA=test-session; bili_jct=test-csrf", TimeSpan.FromSeconds(8));

        Assert.Equal(TimeSpan.FromMilliseconds(3605250), timeProvider.GetLocalNow() - startTime);
        Assert.Empty(cookies["/x/click-interface/click/now"]);
        Assert.Contains("SESSDATA=test-session", cookies["/xlive/web-room/v2/index/getRoomPlayInfo"]);
    }

    /// <summary>
    /// 客户端 UTC 偏差不进入 B 站直播时长。
    /// </summary>
    [Theory]
    [InlineData(-24)]
    [InlineData(37)]
    public async Task ClientClockSkewDoesNotChangeInitialLiveElapsed(int skewDays)
    {
        const long SERVER_NOW = 1792454400;
        var timeProvider = new LiveMarkerTestTimeProvider(DateTimeOffset.FromUnixTimeSeconds(SERVER_NOW).AddDays(skewDays));
        using var handler = new LiveMarkerClockHttpHandler((request, _) => Task.FromResult(
            Json(request.RequestUri!.AbsolutePath == "/x/click-interface/click/now"
                ? "{\"code\":0,\"data\":{\"now\":" + SERVER_NOW + "}}"
                : "{\"code\":0,\"data\":{\"live_status\":1,\"live_time\":" + (SERVER_NOW - 7200) + "}}")));
        using var client = new HttpClient(handler);
        var service = new BilibiliLiveMarkerClockService(client, timeProvider);

        var startTime = await service.CreateStartTimeAsync("100", null, TimeSpan.FromSeconds(8));
        var clock = new LiveMarkerClock(timeProvider);
        clock.Start(startTime);

        Assert.Equal(TimeSpan.FromHours(2), clock.Elapsed);
    }

    /// <summary>
    /// 下播、轮播、缺失时间和未来开播时间均不能生成参考时钟。
    /// </summary>
    [Theory]
    [InlineData("{\"live_status\":0,\"live_time\":1792450800}")]
    [InlineData("{\"live_status\":2,\"live_time\":1792450800}")]
    [InlineData("{\"live_status\":1}")]
    [InlineData("{\"live_status\":1,\"live_time\":-1}")]
    [InlineData("{\"live_status\":1,\"live_time\":1792454401}")]
    [InlineData("{\"live_status\":1,\"live_time\":9223372036854775807}")]
    public async Task RejectsInvalidRoomClock(string roomData)
    {
        var timeProvider = new LiveMarkerTestTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1792454400));
        using var handler = new LiveMarkerClockHttpHandler((request, _) => Task.FromResult(
            Json(request.RequestUri!.AbsolutePath == "/x/click-interface/click/now"
                ? "{\"code\":0,\"data\":{\"now\":1792454400}}"
                : "{\"code\":0,\"data\":" + roomData + "}")));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BilibiliLiveMarkerClockService(client, timeProvider).CreateStartTimeAsync("100", null, TimeSpan.FromSeconds(8)));
    }

    /// <summary>
    /// 服务器时间缺失或非法时拒绝创建，不以本机时间回退。
    /// </summary>
    [Theory]
    [InlineData("{\"code\":0,\"data\":null}")]
    [InlineData("{\"code\":0,\"data\":{}}")]
    [InlineData("{\"code\":0,\"data\":{\"now\":-1}}")]
    [InlineData("{\"code\":0,\"data\":{\"now\":9223372036854775807}}")]
    public async Task RejectsInvalidServerClock(string json)
    {
        var requests = 0;
        using var handler = new LiveMarkerClockHttpHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(Json(json));
        });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BilibiliLiveMarkerClockService(client).CreateStartTimeAsync("100", null, TimeSpan.FromSeconds(8)));

        Assert.Equal(1, requests);
    }

    /// <summary>
    /// 无效房间号在发出请求前被拒绝。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task RejectsInvalidRoomIdWithoutHttp(string roomId)
    {
        using var handler = new LiveMarkerClockHttpHandler((_, _) => throw new InvalidOperationException("unexpected HTTP"));
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BilibiliLiveMarkerClockService(client).CreateStartTimeAsync(roomId, null, TimeSpan.FromSeconds(8)));

        Assert.NotEqual("unexpected HTTP", exception.Message);
    }

    /// <summary>
    /// API 业务错误保留服务端错误信息。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreservesApiBusinessError(bool serverFails)
    {
        using var handler = new LiveMarkerClockHttpHandler((request, _) =>
        {
            var isServerRequest = request.RequestUri!.AbsolutePath == "/x/click-interface/click/now";
            return Task.FromResult(Json(isServerRequest == serverFails
                ? "{\"code\":-412,\"message\":\"offline fixture error\"}"
                : "{\"code\":0,\"data\":{\"now\":1792454400}}"));
        });
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BilibiliLiveMarkerClockService(client).CreateStartTimeAsync("100", null, TimeSpan.FromSeconds(8)));

        Assert.Equal("offline fixture error", exception.Message);
    }

    /// <summary>
    /// HTTP 错误不会被转换成成功的本地参考时间。
    /// </summary>
    [Fact]
    public async Task RejectsHttpError()
    {
        using var handler = new LiveMarkerClockHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new BilibiliLiveMarkerClockService(client).CreateStartTimeAsync("100", null, TimeSpan.FromSeconds(8)));
    }

    /// <summary>
    /// 房间请求取消时立即结束，不返回本机时间回退结果。
    /// </summary>
    [Fact]
    public async Task CancellationStopsPendingRoomRequest()
    {
        var roomRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new LiveMarkerClockHttpHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/x/click-interface/click/now")
            {
                return Json("{\"code\":0,\"data\":{\"now\":1792454400}}");
            }

            roomRequested.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json("{}");
        });
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var pending = new BilibiliLiveMarkerClockService(client).CreateStartTimeAsync("100", null,
            TimeSpan.FromSeconds(8), cancellation.Token);
        await roomRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private static HttpResponseMessage Json(string value)
    {
        return new(HttpStatusCode.OK) { Content = new StringContent(value) };
    }
}
