using System.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证账号状态与统一发送队列、限频重试和已接受进度的边界。</summary>
public class AccountSendingIntegrationTests
{
    /// <summary>连续屏蔽不锁定账号或发送队列，响应恢复后原内容和新内容均可继续发送。</summary>
    [Theory]
    [InlineData("""{"code":0,"message":"f","data":{}}""")]
    [InlineData("""{"code":0,"msg":"f","data":{}}""")]
    public async Task RepeatedBlockedResponsesAllowLaterRecoveryAndNewMessages(string blockedResponse)
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        var account = CreateAccount();
        sessions.Register(account);
        using var handler = new DanmuSendHttpHandler { ResponseJson = """{"code":0,"data":{}}""" };
        for (var index = 0; index < 3; index++)
        {
            handler.ResponseBodies.Enqueue(blockedResponse);
        }

        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sender = new DanmuSendService(client, new FakeBilibiliDanmuConfigService(), accountSessions: sessions);
        var settings = new AppSettings { SendIntervalMs = 0 };
        var request = CreateRequest("同传内容");
        DanmuDeliveryProgress? progress = null;
        for (var index = 0; index < 3; index++)
        {
            var blocked = await sender.SendAsync(request, account, settings, progress, cancellation.Token);
            progress = blocked.Progress;

            Assert.False(blocked.IsComplete);
            var failure = Assert.Single(blocked.Results);
            Assert.Equal(0, failure.ApiCode);
            Assert.Equal(DanmuSendFailureKind.Blocked, failure.FailureKind);
            Assert.Equal(index + 1, handler.RequestCount);
            Assert.True(sessions.CanSend(account.Id, account.Cookie));
        }

        var recovered = await sender.SendAsync(request, account, settings, progress, cancellation.Token);
        var next = await sender.SendAsync(CreateRequest("不同的同传内容"), account, settings,
            cancellationToken: cancellation.Token);

        Assert.True(recovered.IsComplete);
        Assert.True(Assert.Single(recovered.Results).IsAccepted);
        Assert.True(next.IsComplete);
        Assert.True(Assert.Single(next.Results).IsAccepted);
        Assert.Equal(5, handler.RequestCount);
        Assert.Equal(["同传内容", "同传内容", "同传内容", "同传内容", "不同的同传内容"],
            handler.Forms.Select(form => form["msg"]));
        Assert.True(sessions.CanSend(account.Id, account.Cookie));
    }

    /// <summary>真实传输层的未登录响应保留失败历史并阻止同一凭据后续发送。</summary>
    [Fact]
    public async Task AuthenticationFailureIsRecordedAndBlocksLaterRequests()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        var account = CreateAccount();
        sessions.Register(account);
        using var handler = new DanmuSendHttpHandler { ResponseJson = """{"code":-101,"message":"未登录"}""" };
        using var client = new HttpClient(handler);
        var config = new FakeBilibiliDanmuConfigService();
        var sender = new DanmuSendService(client, config, accountSessions: sessions);

        var first = await sender.SendAsync(CreateRequest("第一条"), account, new() { SendIntervalMs = 0 });
        var second = await sender.SendAsync(CreateRequest("第二条"), account, new() { SendIntervalMs = 0 });

        Assert.Equal(-101, Assert.Single(first.Results).ApiCode);
        Assert.Equal(DanmuSendFailureKind.Rejected, first.Results[0].FailureKind);
        Assert.Equal(AccountValidationStatus.Expired, sessions.GetState(account.Id).Status);
        Assert.False(second.IsComplete);
        Assert.Equal(DanmuSendFailureKind.LocalFailure, Assert.Single(second.Results).FailureKind);
        Assert.Equal(1, handler.RequestCount);
        Assert.Single(config.GetRequests);
    }

    /// <summary>读取账号配置时已确认未登录，也会封锁该凭据且不进入发送端点。</summary>
    [Fact]
    public async Task ConfigurationAuthenticationFailureBlocksTheSameCredential()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        var account = CreateAccount();
        sessions.Register(account);
        using var handler = new DanmuConfigHttpHandler();
        handler.Responses.Enqueue("""{"code":-101,"message":"未登录"}""");
        using var client = new HttpClient(handler);
        var transport = new ScriptedDanmuSendTransport();
        var sender = new DanmuSendService(danmuConfigService: new BilibiliDanmuConfigService(client),
            transport: transport, accountSessions: sessions);

        var first = await sender.SendAsync(CreateRequest("第一条"), account, new() { SendIntervalMs = 0 });
        var second = await sender.SendAsync(CreateRequest("第二条"), account, new() { SendIntervalMs = 0 });

        Assert.False(first.IsComplete);
        Assert.False(second.IsComplete);
        Assert.Equal(AccountValidationStatus.Expired, sessions.GetState(account.Id).Status);
        Assert.Single(handler.Requests);
        Assert.Empty(transport.Messages);
    }

    /// <summary>Cookie 编辑立即撤销旧凭据的限频退避，新凭据可以独立发送。</summary>
    [Fact]
    public async Task CookieChangeCancelsBackoffWithoutSendingOldCredentialsAgain()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        var account = CreateAccount();
        sessions.Register(account);
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited });
        var clock = new ManualSendTimeProvider();
        var sender = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService(),
            transport: transport, timeProvider: clock, accountSessions: sessions);
        var settings = new AppSettings { SendIntervalMs = 0 };
        var sending = sender.SendAsync(CreateRequest("旧凭据"), account, settings);
        await clock.WaitForTimerAsync();

        account.Cookie = "SESSDATA=new;bili_jct=new";
        var cancelled = await sending;
        clock.Advance(TimeSpan.FromMinutes(1));
        var next = await sender.SendAsync(CreateRequest("新凭据"), account, settings);

        Assert.False(cancelled.IsComplete);
        Assert.Equal(DanmuSendFailureKind.LocalFailure, Assert.Single(cancelled.Results).FailureKind);
        Assert.True(next.IsComplete);
        Assert.Equal(["旧凭据", "新凭据"], transport.Messages);
    }

    /// <summary>撤销队列中间账号不会越过尚在读取配置的前项，也不会启动旧请求。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokingQueuedAccountPreservesQueueOrder(bool removeAccount)
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        var account = CreateAccount();
        var queuedAccount = CreateAccount("queued");
        sessions.Register(account);
        sessions.Register(queuedAccount);
        var configReady = new TaskCompletionSource<DanmuRoomConfig>(TaskCreationOptions.RunContinuationsAsynchronously);
        var config = new FakeBilibiliDanmuConfigService();
        config.ReadAsync = (_, _, _, _) => config.GetRequests.Count == 1
            ? configReady.Task : Task.FromResult(config.Config);
        var transport = new ScriptedDanmuSendTransport();
        var sender = new DanmuSendService(danmuConfigService: config, transport: transport, accountSessions: sessions);
        var settings = new AppSettings { SendIntervalMs = 0 };
        var first = sender.SendAsync(CreateRequest("先发"), account, settings);
        var revoked = sender.SendAsync(CreateRequest("撤销"), queuedAccount, settings);
        var last = sender.SendAsync(CreateRequest("后发"), account, settings);

        if (removeAccount)
        {
            sessions.Remove(queuedAccount.Id);
        }
        else
        {
            queuedAccount.Cookie = "SESSDATA=changed;bili_jct=changed";
        }

        Assert.False((await revoked).IsComplete);
        Assert.False(last.IsCompleted);
        Assert.Empty(transport.Messages);
        Assert.Single(config.GetRequests);
        configReady.SetResult(config.Config);

        Assert.True((await first).IsComplete);
        Assert.True((await last).IsComplete);
        Assert.Equal(["先发", "后发"], transport.Messages);
        Assert.Equal(2, config.GetRequests.Count);
    }

    /// <summary>验证失效发生在退避期间时，重试前重新检查共享账号状态。</summary>
    [Fact]
    public async Task AccountBecomingExpiredDuringBackoffPreventsNextAttempt()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        var account = CreateAccount();
        sessions.Register(account);
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited });
        var clock = new ManualSendTimeProvider();
        var sender = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService(),
            transport: transport, timeProvider: clock, accountSessions: sessions);
        var sending = sender.SendAsync(CreateRequest("正文"), account, new() { SendIntervalMs = 0 });
        await clock.WaitForTimerAsync();

        sessions.ReportAuthenticationFailure(account.Id, account.Cookie);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.False((await sending).IsComplete);
        Assert.Single(transport.Messages);
    }

    /// <summary>Cookie 变化时已明确接受的分段仍保留，剩余正文停止发送。</summary>
    [Fact]
    public async Task ConfirmedSuccessBeforeCredentialRevocationKeepsAcceptedProgress()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        var account = CreateAccount();
        sessions.Register(account);
        var transport = new ScriptedDanmuSendTransport
        {
            BeforeResponse = () => account.Cookie = "SESSDATA=changed;bili_jct=changed"
        };
        var sender = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService
        {
            Config = new() { Length = 4 }
        }, transport: transport, accountSessions: sessions);

        var result = await sender.SendAsync(CreateRequest("甲乙丙丁戊己"), account, new() { SendIntervalMs = 0 });

        Assert.False(result.IsComplete);
        Assert.Equal(4, result.Progress.GetAcceptedLength(0));
        Assert.True(result.Results[0].IsAccepted);
        Assert.False(result.Results[1].IsAccepted);
        Assert.Equal(["甲乙丙丁"], transport.Messages);
    }

    private static BilibiliAccount CreateAccount(string id = "account")
    {
        return new() { Id = id, Cookie = "SESSDATA=test;bili_jct=test" };
    }

    private static DanmuSendRequest CreateRequest(string body)
    {
        return new() { RoomId = "100", Contents = [new() { Body = body }] };
    }
}
