using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 用户名查询的离线接口协议测试。
/// </summary>
public class BilibiliUserNameServiceTests
{
    /// <summary>
    /// 按 UID 匿名查询公开名片，并返回用户名。
    /// </summary>
    [Fact]
    public async Task GetUserNameUsesPublicCardWithoutCookie()
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("""{"code":0,"data":{"card":{"mid":"42","name":"同传用户"}}}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliUserNameService(client);

        var name = await service.GetUserNameAsync(42);

        Assert.Equal("同传用户", name);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/x/web-interface/card", request.AbsolutePath);
        Assert.Equal("?mid=42", request.Query);
        Assert.Equal("", handler.Cookie);
    }

    /// <summary>
    /// 保留业务错误说明，缺失或错配的名片不能显示成当前用户。
    /// </summary>
    [Theory]
    [InlineData("{\"code\":-400,\"message\":\"用户不存在\"}", "用户不存在")]
    [InlineData("{\"code\":-400,\"message\":\"\",\"msg\":\"用户不存在\"}", "用户不存在")]
    [InlineData("{\"code\":0,\"data\":null}", "未获取到用户名")]
    [InlineData("{\"code\":0,\"data\":{\"card\":{\"mid\":42,\"name\":\" \"}}}", "未获取到用户名")]
    [InlineData("{\"code\":0,\"data\":{\"card\":{\"mid\":43,\"name\":\"其他用户\"}}}", "未获取到用户名")]
    public async Task InvalidCardOrApiFailureIsReported(string response, string message)
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue(response);
        using var client = new HttpClient(handler);
        var service = new BilibiliUserNameService(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetUserNameAsync(42));

        Assert.Equal(message, exception.Message);
    }

    /// <summary>
    /// 非正数 UID 不产生网络请求。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidUidDoesNotSendRequest(long uid)
    {
        using var handler = new ModerationHttpHandler();
        using var client = new HttpClient(handler);
        var service = new BilibiliUserNameService(client);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetUserNameAsync(uid));

        Assert.Empty(handler.Requests);
    }
}
