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
    /// 写接口使用正确房间、目标、原始弹幕、Cookie 和 CSRF。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("禁言内容 & + = ? % 中文🙂")]
    public async Task MuteSendsAccountCookieAndCsrf(string message)
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("""{"code":0,"data":{}}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);

        await service.MuteUserAsync(100, 30, "SESSDATA=test-session;bili_jct=test-csrf;DedeUserID=10", message);

        Assert.Equal("https://api.live.bilibili.com/xlive/web-ucenter/v1/banned/AddSilentUser",
            handler.Requests.Single().AbsoluteUri);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("application/x-www-form-urlencoded", handler.ContentType);
        Assert.Equal("100", handler.FormParameters["room_id"]);
        Assert.Equal("30", handler.FormParameters["tuid"]);
        Assert.Equal("web", handler.FormParameters["mobile_app"]);
        Assert.Equal(message, handler.FormParameters["msg"]);
        Assert.Equal("test-csrf", handler.FormParameters["csrf"]);
        Assert.Equal("test-csrf", handler.FormParameters["csrf_token"]);
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MuteUserAsync(100, 30, cookie, "目标弹幕"));
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// 操作响应的空对象、空数组和空值都不会影响成功状态。
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task MuteAcceptsEmptyOperationData(string data)
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue($$"""{"code":0,"message":"","data":{{data}}}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);

        await service.MuteUserAsync(100, 30, "SESSDATA=test;bili_jct=csrf", "目标弹幕");

        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// 禁言失败保留服务端错误，优先使用非空的 message，再使用 msg。
    /// </summary>
    [Theory]
    [InlineData("{\"code\":-1,\"message\":\"没有权限\"}", "没有权限")]
    [InlineData("{\"code\":-1,\"message\":\"没有权限\",\"msg\":\"备用错误\"}", "没有权限")]
    [InlineData("{\"code\":-1,\"message\":\"\",\"msg\":\"没有权限\",\"data\":[]}", "没有权限")]
    [InlineData("{\"code\":-1,\"message\":\" \",\"msg\":\"没有权限\"}", "没有权限")]
    [InlineData("{\"code\":-1,\"message\":null,\"msg\":\"没有权限\"}", "没有权限")]
    [InlineData("{\"code\":-1,\"message\":\"\",\"msg\":\"\"}", "禁言失败")]
    public async Task MutePreservesApiError(string response, string expectedMessage)
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue(response);
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MuteUserAsync(100, 30, "SESSDATA=test;bili_jct=csrf", "目标弹幕"));
        Assert.Equal(expectedMessage, exception.Message);
    }
}
