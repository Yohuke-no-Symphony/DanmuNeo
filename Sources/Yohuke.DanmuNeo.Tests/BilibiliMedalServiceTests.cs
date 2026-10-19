using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证牌墙、真实主播查询和佩戴接口的离线协议。</summary>
public class BilibiliMedalServiceTests
{
    /// <summary>牌墙使用已验证账号UID，保留服务器当前佩戴状态。</summary>
    [Fact]
    public async Task WallUsesValidatedUidAndKeepsAccountLevelWearingStatus()
    {
        using var handler = new MedalHttpHandler("""
            {"code":0,"data":{"count":1,"list":[{"medal_info":{"medal_id":11,"target_id":100,"medal_name":"夜更","level":20,"wearing_status":1},"target_name":"主播"}]}}
            """);
        using var client = new HttpClient(handler);
        var service = new BilibiliMedalService(client);
        var result = await service.GetMedalsAsync(42, "SESSDATA=fake;bili_jct=fake");
        Assert.True(result.IsComplete);
        var medal = Assert.Single(result.Items);
        Assert.Equal(100, medal.TargetUid);
        Assert.True(medal.IsWearing);
        Assert.Contains("target_id=42", Assert.Single(handler.Uris));
        Assert.Contains("SESSDATA=fake", Assert.Single(handler.Cookies));
    }

    /// <summary>缺总数或数量不足不能证明账号未持有其他牌子。</summary>
    [Theory]
    [InlineData("{\"list\":[]}")]
    [InlineData("{\"count\":2,\"list\":[]}")]
    [InlineData("{\"count\":0,\"list\":[],\"has_more\":true}")]
    public async Task IncompleteWallIsExplicit(string data)
    {
        using var handler = new MedalHttpHandler("{\"code\":0,\"data\":" + data + "}");
        using var client = new HttpClient(handler);
        var result = await new BilibiliMedalService(client).GetMedalsAsync(42, "");
        Assert.False(result.IsComplete);
    }

    /// <summary>佩戴和摘牌都发送真实medal ID与规范CSRF字段。</summary>
    [Fact]
    public async Task WritesUseAuthenticatedForms()
    {
        using var handler = new MedalHttpHandler("{\"code\":0}", "{\"code\":0}");
        using var client = new HttpClient(handler);
        var service = new BilibiliMedalService(client);
        await service.WearAsync(11, "SESSDATA=fake;bili_jct=csrf-value");
        await service.TakeOffAsync(11, "SESSDATA=fake;bili_jct=csrf-value");
        Assert.EndsWith("/wear", handler.Uris[0]);
        Assert.EndsWith("/take_off", handler.Uris[1]);
        Assert.All(handler.Bodies, body =>
        {
            Assert.Contains("medal_id=11", body);
            Assert.Contains("csrf=csrf-value", body);
            Assert.Contains("csrf_token=csrf-value", body);
        });
    }

    /// <summary>匿名房间资料返回真实主播UID，不发送账号Cookie。</summary>
    [Fact]
    public async Task OwnerLookupIsAnonymousAndDoesNotTrustLocalOwnerUid()
    {
        using var handler = new MedalHttpHandler("""{"code":0,"data":{"room_info":{"uid":1234,"room_id":100}}}""");
        using var client = new HttpClient(handler);
        var result = await new BilibiliMedalService(client).GetRoomOwnerUidAsync("100", "SESSDATA=must-not-send");
        Assert.Equal(1234, result);
        Assert.Empty(Assert.Single(handler.Cookies));
        Assert.Contains("room_id=100", Assert.Single(handler.Uris));
    }

    /// <summary>明确未登录与一般牌墙故障保持不同错误类型。</summary>
    [Fact]
    public async Task ExplicitAuthenticationErrorIsTyped()
    {
        using var handler = new MedalHttpHandler("""{"code":-101,"message":"expired"}""");
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<BilibiliAuthenticationException>(() => new BilibiliMedalService(client).GetMedalsAsync(42, ""));
    }
}
