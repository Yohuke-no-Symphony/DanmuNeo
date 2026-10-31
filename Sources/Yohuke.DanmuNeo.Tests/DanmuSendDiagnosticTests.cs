using System.Net;
using System.Text.Json;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证发送结果中的响应诊断脱敏、长度上限与传输失败语义。</summary>
public sealed class DanmuSendDiagnosticTests
{
    /// <summary>响应里的空字段分别保留，不影响业务成功判定。</summary>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", null)]
    [InlineData("0", "")]
    public async Task AcceptedResultKeepsIndependentResponseFields(string? message, string? msg)
    {
        using var handler = new DanmuSendHttpHandler
        {
            ResponseJson = JsonSerializer.Serialize(new { code = 0, message, msg, data = new { } })
        };
        using var client = new HttpClient(handler);
        var sender = new DanmuSendService(client, new FakeBilibiliDanmuConfigService());

        var result = await sender.SendAsync("100", "同传正文", new() { Cookie = "SESSDATA=session;bili_jct=csrf" }, new());

        Assert.True(result.IsAccepted);
        var diagnostic = Assert.IsType<DanmuSendDiagnostic>(result.Diagnostic);
        Assert.Equal(0, diagnostic.ApiCode);
        Assert.Equal(message, diagnostic.ResponseMessage);
        Assert.Equal(msg, diagnostic.ResponseMsg);
        Assert.Null(diagnostic.HttpStatusCode);
    }

    /// <summary>超长响应在脱敏后才截断，截断点不会留下凭据前缀。</summary>
    [Fact]
    public async Task ResponseIsRedactedBeforeApplyingLengthLimit()
    {
        var responseText = new string('x', DanmuSendDiagnostic.MAX_RESPONSE_TEXT_LENGTH - 4) + "session-secret-tail";
        using var handler = new DanmuSendHttpHandler
        {
            ResponseJson = JsonSerializer.Serialize(new { code = 0, message = "f", msg = responseText, data = new { } })
        };
        using var client = new HttpClient(handler);
        var sender = new DanmuSendService(client, new FakeBilibiliDanmuConfigService());

        var result = await sender.SendAsync("100", "同传正文", new() { Cookie = "SESSDATA=session-secret-tail;bili_jct=csrf-secret" }, new());

        Assert.False(result.IsAccepted);
        var diagnostic = Assert.IsType<DanmuSendDiagnostic>(result.Diagnostic);
        Assert.Equal("f", diagnostic.ResponseMessage);
        Assert.True(diagnostic.ResponseMsg!.Length <= DanmuSendDiagnostic.MAX_RESPONSE_TEXT_LENGTH + 1);
        Assert.EndsWith("…", diagnostic.ResponseMsg);
        Assert.DoesNotContain("sess", diagnostic.ResponseMsg);
        Assert.DoesNotContain("session-secret-tail", diagnostic.ToLogDetails());
    }

    /// <summary>诊断上限不截短原有业务失败提示。</summary>
    [Fact]
    public async Task DiagnosticLimitDoesNotTruncateUserFacingError()
    {
        var message = new string('x', DanmuSendDiagnostic.MAX_RESPONSE_TEXT_LENGTH + 100);
        using var handler = new DanmuSendHttpHandler
        {
            ResponseJson = JsonSerializer.Serialize(new { code = 10030, message, data = new { } })
        };
        using var client = new HttpClient(handler);
        var sender = new DanmuSendService(client, new FakeBilibiliDanmuConfigService());

        var result = await sender.SendAsync("100", "同传正文", new() { Cookie = "SESSDATA=session;bili_jct=csrf" }, new());

        Assert.False(result.IsAccepted);
        Assert.Equal(message, result.ErrorMessage);
        var diagnostic = Assert.IsType<DanmuSendDiagnostic>(result.Diagnostic);
        Assert.Equal(DanmuSendDiagnostic.MAX_RESPONSE_TEXT_LENGTH + 1, diagnostic.ResponseMessage!.Length);
        Assert.EndsWith("…", diagnostic.ResponseMessage);
    }

    /// <summary>HTTP 失败保留实际状态和请求摘要，不伪造业务码或丢弃异常。</summary>
    [Fact]
    public async Task HttpFailureKeepsRequestDiagnosticAndUnknownBusinessOutcome()
    {
        using var handler = new DanmuSendHttpHandler { StatusCode = HttpStatusCode.BadGateway, ResponseJson = "{}" };
        using var client = new HttpClient(handler);
        var sender = new DanmuSendService(client, new FakeBilibiliDanmuConfigService());

        var result = await sender.SendAsync("100", "同传正文", new() { Cookie = "SESSDATA=session;bili_jct=csrf" }, new());

        Assert.Equal(DanmuSendFailureKind.OutcomeUnknown, result.FailureKind);
        Assert.IsType<HttpRequestException>(result.Error);
        Assert.Null(result.ApiCode);
        var diagnostic = Assert.IsType<DanmuSendDiagnostic>(result.Diagnostic);
        Assert.Equal(502, diagnostic.HttpStatusCode);
        Assert.Null(diagnostic.ApiCode);
        Assert.Null(diagnostic.ResponseMessage);
        Assert.Null(diagnostic.ResponseMsg);
        Assert.Equal(100, diagnostic.RoomId);
        Assert.DoesNotContain("同传正文", diagnostic.ToLogDetails());
    }
}
