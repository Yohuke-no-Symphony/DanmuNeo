using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 主播搜索的离线协议测试。
/// </summary>
public class BilibiliLiveRoomSearchServiceTests
{
    /// <summary>
    /// 搜索传递分页和账号，清理高亮并过滤无效与重复房间。
    /// </summary>
    [Fact]
    public async Task SearchMapsResultsAndSendsPageAndCookie()
    {
        using var handler = new LiveRoomSearchHttpHandler
        {
            ResponseJson = """
                           {"code":0,"data":{"page":2,"numPages":3,"result":[
                             {"uid":42,"roomid":100,"uname":"<em class=\"keyword\">猫</em>&amp;主播","title":"直播&lt;标题&gt;","is_live":true},
                             {"uid":42,"roomid":100,"uname":"重复"},
                             {"uid":0,"roomid":200,"uname":"无UID"},
                             {"uid":43,"roomid":0,"uname":"无房间"}
                           ]}}
                           """
        };
        using var client = new HttpClient(handler);
        var service = new BilibiliLiveRoomSearchService(client);

        var page = await service.SearchAsync("猫 & 主播", 2, "buvid3=test-cookie", TimeSpan.FromSeconds(2));

        var result = Assert.Single(page.Results);
        Assert.Equal("100", result.RoomId);
        Assert.Equal("42", result.OwnerUid);
        Assert.Equal("猫&主播", result.OwnerName);
        Assert.Equal("直播<标题>", result.Title);
        Assert.True(result.IsLive);
        Assert.Equal(2, page.Page);
        Assert.True(page.HasMore);
        Assert.Contains("page=2", handler.RequestUri!.Query);
        Assert.Contains("search_type=live_user", handler.RequestUri.Query);
        Assert.Contains("keyword=猫 & 主播", Uri.UnescapeDataString(handler.RequestUri.Query.Replace("+", " ")));
        Assert.Equal("buvid3=test-cookie", handler.Cookie);
    }

    /// <summary>
    /// API 业务错误保留服务端说明。
    /// </summary>
    [Fact]
    public async Task SearchPreservesBusinessError()
    {
        using var handler = new LiveRoomSearchHttpHandler
        {
            ResponseJson = """{"code":-412,"message":"请求被拦截"}"""
        };
        using var client = new HttpClient(handler);
        var service = new BilibiliLiveRoomSearchService(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchAsync("主播"));

        Assert.Equal("请求被拦截", exception.Message);
    }

    /// <summary>
    /// 缺少结果字段的挑战响应不会伪装成无结果。
    /// </summary>
    [Fact]
    public async Task SearchRejectsChallengeResponse()
    {
        using var handler = new LiveRoomSearchHttpHandler
        {
            ResponseJson = """{"code":0,"data":{"v_voucher":"challenge"}}"""
        };
        using var client = new HttpClient(handler);
        var service = new BilibiliLiveRoomSearchService(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchAsync("主播"));
    }

    /// <summary>
    /// 有效空数组表示没有匹配结果。
    /// </summary>
    [Fact]
    public async Task SearchAcceptsEmptyResults()
    {
        using var handler = new LiveRoomSearchHttpHandler
        {
            ResponseJson = """{"code":0,"data":{"page":1,"numPages":0,"result":[]}}"""
        };
        using var client = new HttpClient(handler);
        var service = new BilibiliLiveRoomSearchService(client);

        var page = await service.SearchAsync("不存在的主播");

        Assert.Empty(page.Results);
        Assert.False(page.HasMore);
    }
}
