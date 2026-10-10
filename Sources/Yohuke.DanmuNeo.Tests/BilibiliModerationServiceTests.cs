using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 禁言服务协议与权限测试。
/// </summary>
public class BilibiliModerationServiceTests
{
    /// <summary>
    /// 主播与房管具有权限，普通登录用户没有权限。
    /// </summary>
    [Theory]
    [InlineData(20, false, true)]
    [InlineData(10, true, true)]
    [InlineData(10, false, false)]
    public async Task PermissionUsesServerIdentity(long uid, bool isAdmin, bool expected)
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue(Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            code = 0,
            data = new { uid = 999, info = new { uid }, badge = new { is_room_admin = isAdmin } }
        }));
        handler.Responses.Enqueue("""{"code":0,"data":{"room_info":{"room_id":100,"uid":20}}}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);

        var permission = await service.GetPermissionAsync("1", "SESSDATA=test");

        Assert.Equal(expected, permission.CanModerate);
        Assert.Equal(uid, permission.AccountUid);
        Assert.Equal(100, permission.RoomId);
        Assert.Equal(20, permission.OwnerUid);
    }

    /// <summary>
    /// 缺失身份或业务错误不可授权。
    /// </summary>
    [Theory]
    [InlineData("{\"code\":0,\"data\":{\"uid\":20,\"badge\":{\"is_room_admin\":true}}}")]
    [InlineData("{\"code\":0,\"data\":{\"info\":{\"uid\":0},\"badge\":{\"is_room_admin\":true}}}")]
    [InlineData("{\"code\":-101,\"message\":\"未登录\"}")]
    public async Task MissingIdentityOrApiFailureRejectsPermission(string response)
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue(response);
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPermissionAsync("100", "SESSDATA=test"));
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// 写接口使用正确房间、目标、Cookie 和 CSRF。
    /// </summary>
    [Fact]
    public async Task MuteSendsAccountCookieAndCsrf()
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("""{"code":0,"data":{}}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);

        await service.MuteUserAsync(100, 30, "SESSDATA=test-session;bili_jct=test-csrf;DedeUserID=10");

        Assert.EndsWith("/AddSilentUser", handler.Requests.Single().AbsolutePath);
        Assert.Contains("room_id=100", handler.Form);
        Assert.Contains("tuid=30", handler.Form);
        Assert.Contains("csrf=test-csrf", handler.Form);
        Assert.Contains("csrf_token=test-csrf", handler.Form);
        Assert.Contains("SESSDATA=test-session", handler.Cookie);
    }

    /// <summary>
    /// 缺少写凭据不发请求。
    /// </summary>
    [Theory]
    [InlineData("SESSDATA=test-session")]
    [InlineData("bili_jct=test-csrf")]
    public async Task IncompleteCookieBlocksPost(string cookie)
    {
        using var handler = new ModerationHttpHandler();
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MuteUserAsync(100, 30, cookie));
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// 禁言失败保留服务端错误。
    /// </summary>
    [Fact]
    public async Task MutePreservesApiError()
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("""{"code":-1,"message":"没有权限"}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MuteUserAsync(100, 30, "SESSDATA=test;bili_jct=csrf"));
        Assert.Equal("没有权限", exception.Message);
    }
}
