using System.Net;
using System.Text;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证离线历史查询的协议映射和业务错误。</summary>
public class BilibiliDanmuHistoryServiceTests
{
    /// <summary>历史查询携带登录身份并保留服务端元数据。</summary>
    [Fact]
    public async Task ParsesHistoryAndPreservesServerMetadata()
    {
        using var handler = new DanmuHistoryHttpHandler("""
            {"code":0,"data":{"room":[{"uid":42,"nickname":"用户","text":"[dog]你好",
            "id":"message-id","timestamp":1800000000,"isadmin":1,
            "user":{"medal":{"name":"粉丝","level":12,"guard_level":3}},
            "emots":{"[dog]":{"url":"https://example.test/dog.png"}}}]}}
            """);
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuHistoryService(client);
        var result = await service.GetRecentAsync("100", "SESSDATA=fake;bili_jct=fake;DedeUserID=10");
        var message = Assert.Single(result);
        Assert.Equal("100", message.RoomId);
        Assert.Equal("message-id", message.MessageId);
        Assert.Equal(1800000000, message.Timestamp);
        Assert.Equal("用户", message.UserName);
        Assert.Equal("粉丝", message.MedalName);
        Assert.True(message.IsRoomAdmin);
        Assert.Single(message.Emoticons);
        Assert.Contains("roomid=100", handler.RequestUri);
        Assert.Contains("room_type=0", handler.RequestUri);
        Assert.Contains("SESSDATA=fake", handler.Cookie);
    }

    /// <summary>服务端业务错误不得伪装成成功的空历史。</summary>
    [Fact]
    public async Task BusinessFailureIsNotAnEmptySuccessfulHistory()
    {
        using var handler = new DanmuHistoryHttpHandler("""{"code":-101,"message":"expired"}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuHistoryService(client);
        await Assert.ThrowsAsync<BilibiliAuthenticationException>(() => service.GetRecentAsync("100", ""));
    }
}
