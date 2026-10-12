using System.Net;
using System.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证接口拒绝与本地、网络、取消失败的发送结果分类。
/// </summary>
public class DanmuSendServiceTests
{
    /// <summary>
    /// 接口状态码与错误消息完整传递，只有非零业务码被标记为明确拒绝。
    /// </summary>
    [Theory]
    [InlineData("{\"code\":0,\"data\":{}}", 0, true, "")]
    [InlineData("{\"code\":10030,\"message\":\"风控拒绝\"}", 10030, false, "风控拒绝")]
    [InlineData("{\"code\":-101,\"msg\":\"未登录\"}", -101, false, "未登录")]
    public async Task ApiResponsePreservesExplicitCode(string json, int code, bool accepted, string error)
    {
        using var handler = new DanmuSendHttpHandler { ResponseJson = json };
        using var client = new HttpClient(handler);
        var service = new DanmuSendService(client);
        DanmuFeedItem? record = null;
        service.RecordCreated += (_, item) => record = item;

        var result = await service.SendAsync("100", "同传", CreateAccount(), new());

        Assert.Equal(code, result.ApiCode);
        Assert.Equal(accepted, result.IsAccepted);
        Assert.Equal(!accepted, result.IsExplicitlyRejected);
        Assert.Equal(error, result.ErrorMessage);
        Assert.Same(record, result.Record);
        Assert.Equal(accepted, result.Record.IsSendAccepted);
    }

    /// <summary>
    /// 零状态码携带屏蔽标记时仍视为发送失败，并保留业务码和失败记录。
    /// </summary>
    [Theory]
    [InlineData("{\"code\":0,\"message\":\"f\",\"data\":{}}")]
    [InlineData("{\"code\":0,\"msg\":\"f\",\"data\":{}}")]
    public async Task BlockedMessageDoesNotCountAsAccepted(string json)
    {
        using var handler = new DanmuSendHttpHandler { ResponseJson = json };
        using var client = new HttpClient(handler);
        var service = new DanmuSendService(client);

        var result = await service.SendAsync("100", "同传", CreateAccount(), new());

        Assert.False(result.IsAccepted);
        Assert.Equal(0, result.ApiCode);
        Assert.False(result.IsExplicitlyRejected);
        Assert.Equal("弹幕被屏蔽", result.ErrorMessage);
        Assert.False(result.Record.IsSendAccepted);
        Assert.Equal("弹幕被屏蔽", result.Record.Status);
    }

    /// <summary>
    /// 连接异常、超时、HTTP 错误和损坏响应都没有明确的业务拒绝码。
    /// </summary>
    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("http")]
    [InlineData("json")]
    public async Task TransportFailureDoesNotClaimApiRejection(string mode)
    {
        using var handler = new DanmuSendHttpHandler
        {
            Exception = mode switch
            {
                "network" => new HttpRequestException("连接失败"),
                "timeout" => new TaskCanceledException("请求超时"),
                _ => null
            },
            StatusCode = mode == "http" ? HttpStatusCode.BadGateway : HttpStatusCode.OK,
            ResponseJson = "损坏的响应"
        };
        using var client = new HttpClient(handler);
        var service = new DanmuSendService(client);

        var result = await service.SendAsync("100", "同传", CreateAccount(), new());

        Assert.False(result.IsAccepted);
        Assert.Null(result.ApiCode);
        Assert.False(result.IsExplicitlyRejected);
        Assert.NotEmpty(result.ErrorMessage);
    }

    /// <summary>
    /// 未配置账号的本地失败不会请求接口或声称接口拒绝。
    /// </summary>
    [Fact]
    public async Task MissingAccountDoesNotClaimApiRejection()
    {
        using var handler = new DanmuSendHttpHandler();
        using var client = new HttpClient(handler);
        var service = new DanmuSendService(client);

        var result = await service.SendAsync("100", "同传", null, new());

        Assert.Equal(0, handler.RequestCount);
        Assert.False(result.IsAccepted);
        Assert.Null(result.ApiCode);
        Assert.False(result.IsExplicitlyRejected);
        Assert.Equal("未配置账号", result.ErrorMessage);
    }

    /// <summary>
    /// 用户主动取消保持取消语义，不产生失败记录。
    /// </summary>
    [Fact]
    public async Task CallerCancellationDoesNotCreateRejectedRecord()
    {
        using var handler = new DanmuSendHttpHandler();
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var service = new DanmuSendService(client);
        var records = new List<DanmuFeedItem>();
        service.RecordCreated += (_, item) => records.Add(item);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SendAsync("100", "同传", CreateAccount(), new(), cancellation.Token));

        Assert.Empty(records);
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// 连续发送保留调用方注入的客户端生命周期。
    /// </summary>
    [Fact]
    public async Task InjectedClientRemainsUsableForSubsequentSend()
    {
        using var handler = new DanmuSendHttpHandler { ResponseJson = "{\"code\":0,\"data\":{}}" };
        using var client = new HttpClient(handler);
        var service = new DanmuSendService(client);
        var settings = new AppSettings { SendIntervalMs = 0 };

        Assert.True((await service.SendAsync("100", "第一条", CreateAccount(), settings)).IsAccepted);
        Assert.True((await service.SendAsync("100", "第二条", CreateAccount(), settings)).IsAccepted);
        Assert.Equal(2, handler.RequestCount);
    }

    private static BilibiliAccount CreateAccount()
    {
        return new() { Cookie = "SESSDATA=test;bili_jct=test;DedeUserID=42" };
    }
}
