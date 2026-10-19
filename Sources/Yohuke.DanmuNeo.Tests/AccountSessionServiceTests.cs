using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 确认凭据版本、删除和验证错误不会使失效账号继续发送。
/// </summary>
public class AccountSessionServiceTests
{
    /// <summary>
    /// Cookie 更新后，旧请求的迟到结果不能覆盖新会话。
    /// </summary>
    [Fact]
    public async Task LateResultCannotInvalidateNewCookie()
    {
        var validation = new ControlledAccountValidationService();
        using var sessions = new BilibiliAccountSessionService(validation);
        var account = new BilibiliAccount { Cookie = "SESSDATA=first;bili_jct=csrf" };
        sessions.Register(account);
        var oldCookie = account.Cookie;
        var oldToken = sessions.GetCredentialCancellationToken(account.Id, oldCookie);
        var first = sessions.ValidateAsync(account);
        account.Cookie = "SESSDATA=second;bili_jct=csrf";
        var second = sessions.ValidateAsync(account);
        validation.Requests[1].SetResult(new() { Status = AccountValidationStatus.Valid, Uid = 42, UserName = "有效" });
        await second;
        validation.Requests[0].SetResult(new() { Status = AccountValidationStatus.Expired });
        await first;

        Assert.True(oldToken.IsCancellationRequested);
        Assert.False(sessions.CanSend(account.Id, oldCookie));
        Assert.True(sessions.CanSend(account.Id, account.Cookie));
        Assert.Equal(AccountValidationStatus.Valid, sessions.GetState(account.Id).Status);
        Assert.Equal("账号", account.Name);
    }

    /// <summary>
    /// 暂时无法验证不能解除同一 Cookie 已确认的失效状态。
    /// </summary>
    [Fact]
    public async Task UnavailableDoesNotUnblockKnownExpiredCredentials()
    {
        var validation = new ControlledAccountValidationService();
        using var sessions = new BilibiliAccountSessionService(validation);
        var account = new BilibiliAccount { Cookie = "SESSDATA=offline;bili_jct=csrf" };
        sessions.Register(account);
        sessions.ReportAuthenticationFailure(account.Id, account.Cookie);
        var check = sessions.ValidateAsync(account);
        validation.Requests[0].SetResult(new() { Status = AccountValidationStatus.Unavailable });
        await check;

        Assert.Equal(AccountValidationStatus.Unavailable, sessions.GetState(account.Id).Status);
        Assert.False(sessions.CanSend(account.Id, account.Cookie));
        Assert.Equal("SESSDATA=offline;bili_jct=csrf", account.Cookie);
    }

    /// <summary>
    /// 删除账号会撤销排队请求，迟到校验不能重新注册账号。
    /// </summary>
    [Fact]
    public async Task RemovedAccountCannotBeRevivedByLateValidation()
    {
        var validation = new ControlledAccountValidationService();
        using var sessions = new BilibiliAccountSessionService(validation);
        var account = new BilibiliAccount { Cookie = "SESSDATA=offline;bili_jct=csrf" };
        sessions.Register(account);
        var token = sessions.GetCredentialCancellationToken(account.Id, account.Cookie);
        var check = sessions.ValidateAsync(account);
        sessions.Remove(account.Id);
        validation.Requests[0].SetResult(new() { Status = AccountValidationStatus.Valid, Uid = 42, UserName = "用户" });
        await check;

        Assert.True(token.IsCancellationRequested);
        Assert.False(sessions.CanSend(account.Id, account.Cookie));
    }

    /// <summary>
    /// 验证失效时统一发送服务不会查询配置或发送网络请求。
    /// </summary>
    [Fact]
    public async Task KnownExpiredAccountIsBlockedBeforeSending()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        var account = new BilibiliAccount { Cookie = "SESSDATA=offline;bili_jct=csrf" };
        sessions.Register(account);
        sessions.ReportAuthenticationFailure(account.Id, account.Cookie);
        using var handler = new ModerationHttpHandler();
        using var client = new HttpClient(handler);
        var config = new FakeBilibiliDanmuConfigService();
        var sender = new DanmuSendService(client, config, accountSessions: sessions);

        var result = await sender.SendAsync("100", "同传", account, new());

        Assert.False(result.IsAccepted);
        Assert.Empty(handler.Requests);
        Assert.Empty(config.GetRequests);
    }
}
