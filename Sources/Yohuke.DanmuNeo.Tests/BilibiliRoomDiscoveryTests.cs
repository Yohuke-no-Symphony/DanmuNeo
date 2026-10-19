using System.Threading;
using System.Diagnostics;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证房号、UID 与关注直播间的离线协议和并行边界。</summary>
public class BilibiliRoomDiscoveryTests
{
    private const string NAV_JSON = """{"code":0,"data":{"wbi_img":{"img_url":"https://example.com/abcdefghijklmnopqrstuvwxyz012345.png","sub_url":"https://example.com/012345abcdefghijklmnopqrstuvwxyz.png"}}}""";

    /// <summary>数字双路同时发起，并在命中同一规范房间时合并来源。</summary>
    [Fact]
    public async Task NumericQueriesRunConcurrentlyAndMergeCanonicalRoom()
    {
        var uidStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var roomStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RoomDiscoveryHttpHandler
        {
            RespondAsync = async (uri, token) =>
            {
                if (uri.AbsolutePath.EndsWith("/nav"))
                {
                    return NAV_JSON;
                }

                if (uri.AbsolutePath.EndsWith("/acc/info"))
                {
                    uidStarted.SetResult();
                    await roomStarted.Task.WaitAsync(token);
                    return """{"code":0,"data":{"mid":42,"name":"主播","live_room":{"roomid":100,"liveStatus":1}}}""";
                }

                if (uri.Query.Contains("room_id=42"))
                {
                    roomStarted.SetResult();
                    await uidStarted.Task.WaitAsync(token);
                }

                return RoomJson(100, 42);
            }
        };
        using var client = new HttpClient(handler);
        var service = new BilibiliLiveRoomSearchService(client);

        var page = await service.SearchAsync("00042", cookie: "SESSDATA=test", timeout: TimeSpan.FromSeconds(2));

        var result = Assert.Single(page.Results);
        Assert.Equal("100", result.RoomId);
        Assert.Equal("42", result.ShortRoomId);
        Assert.Equal(LiveRoomSearchSource.UserId | LiveRoomSearchSource.RoomId, result.Sources);
        Assert.Empty(page.Issues);
        Assert.False(page.HasMore);
        Assert.All(handler.Requests, request => Assert.Empty(request.Cookie));
        Assert.Contains(handler.Requests, request => request.Uri.AbsolutePath.EndsWith("/acc/info") && request.Uri.Query.Contains("w_rid="));
    }

    /// <summary>纯数字可能分别命中两个房间，保留两项及各自来源。</summary>
    [Fact]
    public async Task NumericQueriesKeepDistinctUidAndRoomMatches()
    {
        using var handler = new RoomDiscoveryHttpHandler
        {
            RespondAsync = (uri, _) => Task.FromResult(uri.AbsolutePath.EndsWith("/nav") ? NAV_JSON
                : uri.AbsolutePath.EndsWith("/acc/info")
                    ? """{"code":0,"data":{"mid":42,"name":"主播","live_room":{"roomid":100}}}"""
                    : uri.Query.Contains("room_id=42") ? RoomJson(200, 42) : RoomJson(100, 0))
        };
        using var client = new HttpClient(handler);

        var page = await new BilibiliLiveRoomSearchService(client).SearchAsync("42");

        Assert.Equal(2, page.Results.Count);
        Assert.Equal(LiveRoomSearchSource.UserId, page.Results.Single(result => result.RoomId == "100").Sources);
        Assert.Equal(LiveRoomSearchSource.RoomId, page.Results.Single(result => result.RoomId == "200").Sources);
    }

    /// <summary>一个端点失败不会丢失另一条路的结果，且保留业务错误码。</summary>
    [Fact]
    public async Task NumericPartialFailureKeepsSuccessfulResultAndIssue()
    {
        using var handler = new RoomDiscoveryHttpHandler
        {
            RespondAsync = (uri, _) => Task.FromResult(uri.AbsolutePath.EndsWith("/nav") ? NAV_JSON
                : uri.AbsolutePath.EndsWith("/acc/info")
                    ? """{"code":-412,"message":"请求被拦截"}""" : RoomJson(100, 42))
        };
        using var client = new HttpClient(handler);

        var page = await new BilibiliLiveRoomSearchService(client).SearchAsync("42");

        Assert.Equal(LiveRoomSearchSource.RoomId, Assert.Single(page.Results).Sources);
        var issue = Assert.Single(page.Issues);
        Assert.Equal(LiveRoomSearchSource.UserId, issue.Source);
        Assert.Equal(-412, issue.ApiCode);
        Assert.Equal("请求被拦截", issue.Message);
    }

    /// <summary>空词只请求关注直播列表，过滤无效并合并重复房间。</summary>
    [Fact]
    public async Task EmptyQueryLoadsFollowedLiveRoomsWithCookie()
    {
        using var handler = new RoomDiscoveryHttpHandler
        {
            RespondAsync = (_, _) => Task.FromResult("""{"code":0,"data":{"live_users":{"items":[{"mid":42,"room_id":100,"uname":"主播&amp;昵称","face":"https://example.com/avatar.png"},{"mid":42,"room_id":100},{"mid":0,"room_id":200}]}}}""")
        };
        using var client = new HttpClient(handler);

        var page = await new BilibiliLiveRoomSearchService(client).SearchAsync("  ", cookie: "SESSDATA=test");

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/x/polymer/web-dynamic/v1/portal", request.Uri.AbsolutePath);
        Assert.Contains("SESSDATA=test", request.Cookie);
        var result = Assert.Single(page.Results);
        Assert.Equal("主播&昵称", result.OwnerName);
        Assert.True(result.IsLive);
        Assert.Equal(LiveRoomSearchSource.Following, result.Sources);
        Assert.False(page.HasMore);
    }

    /// <summary>关注查询缺少 Cookie 时给出登录提示且不发出 HTTP。</summary>
    [Fact]
    public async Task EmptyAnonymousQueryDoesNotAccessNetwork()
    {
        using var handler = new RoomDiscoveryHttpHandler();
        using var client = new HttpClient(handler);

        var page = await new BilibiliLiveRoomSearchService(client).SearchAsync("");

        Assert.Empty(handler.Requests);
        Assert.Empty(page.Results);
        Assert.Equal(-101, Assert.Single(page.Issues).ApiCode);
    }

    /// <summary>关注列表的登录过期保留可识别的错误码。</summary>
    [Fact]
    public async Task FollowingExpiredCookieProducesAuthenticationIssue()
    {
        using var handler = new RoomDiscoveryHttpHandler
        {
            RespondAsync = (_, _) => Task.FromResult("""{"code":-101,"message":"账号未登录"}""")
        };
        using var client = new HttpClient(handler);

        var page = await new BilibiliLiveRoomSearchService(client).SearchAsync("", cookie: "SESSDATA=test");

        Assert.Equal(-101, Assert.Single(page.Issues).ApiCode);
        Assert.Contains("登录", page.Issues[0].Message);
    }

    /// <summary>数字超过 Int64 范围时不能误作昵称查询。</summary>
    [Fact]
    public async Task OutOfRangeNumericQueryDoesNotAccessNetwork()
    {
        using var handler = new RoomDiscoveryHttpHandler();
        using var client = new HttpClient(handler);

        var page = await new BilibiliLiveRoomSearchService(client).SearchAsync(new string('9', 40));

        Assert.Empty(handler.Requests);
        Assert.NotEmpty(page.Issues);
    }

    /// <summary>WBI 取键使用注入客户端和本次超时，取消不伪装成双路失败。</summary>
    [Fact]
    public async Task NumericCancellationPropagatesAndWbiUsesInjectedTimeout()
    {
        using var handler = new RoomDiscoveryHttpHandler
        {
            RespondAsync = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "";
            }
        };
        using var client = new HttpClient(handler);
        var service = new BilibiliLiveRoomSearchService(client);
        var timer = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchAsync("42",
            timeout: TimeSpan.FromSeconds(2), cancellationToken: cancellation.Token));

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Contains(handler.Requests, request => request.Uri.AbsolutePath.EndsWith("/nav"));
    }

    /// <summary>WBI 取键也受请求超时约束，超时保留为查询失败。</summary>
    [Fact]
    public async Task WbiRequestUsesDiscoveryTimeout()
    {
        using var handler = new RoomDiscoveryHttpHandler
        {
            RespondAsync = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "";
            }
        };
        using var client = new HttpClient(handler);
        var timer = Stopwatch.StartNew();

        var page = await new BilibiliLiveRoomSearchService(client).SearchAsync("42", timeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(2, page.Issues.Count);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Contains(handler.Requests, request => request.Uri.AbsolutePath.EndsWith("/nav"));
    }

    private static string RoomJson(long roomId, long shortId)
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            code = 0,
            data = new
            {
                room_info = new { room_id = roomId, short_id = shortId, uid = 42, title = "直播标题", live_status = 1 },
                anchor_info = new { base_info = new { uname = "主播" } }
            }
        });
    }
}
