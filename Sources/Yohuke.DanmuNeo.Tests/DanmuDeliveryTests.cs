using System.Threading;
using System.Net;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证逻辑发送的上限、重试、进度与历史语义。
/// </summary>
public class DanmuDeliveryTests
{
    /// <summary>实际账号上限控制分段，每段保留完整标识符。</summary>
    [Fact]
    public async Task SegmentsUseAccountLimitAndRepeatCompleteMarks()
    {
        var transport = new ScriptedDanmuSendTransport();
        var config = new FakeBilibiliDanmuConfigService { Config = new() { Length = 8 } };
        var service = new DanmuSendService(danmuConfigService: config, transport: transport);
        var request = CreateRequest("甲乙丙丁戊己庚辛");

        var result = await service.SendAsync(request, CreateAccount(), new() { SendIntervalMs = 0 });

        Assert.True(result.IsComplete);
        Assert.Equal(["【甲乙丙丁戊己】", "【…庚辛】"], transport.Messages);
        Assert.All(transport.Messages, text => Assert.True(text.Length <= 8));
    }

    /// <summary>分段不会拆开代理对、组合字符或连接表情，也不会遗漏正文。</summary>
    [Theory]
    [InlineData("😀😀😀", 7)]
    [InlineData("e\u0301e\u0301e\u0301", 7)]
    [InlineData("👩‍💻👩‍💻", 10)]
    public async Task SegmentationKeepsTextElementsAndAllBodyContent(string body, int limit)
    {
        var transport = new ScriptedDanmuSendTransport();
        var service = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService
        {
            Config = new() { Length = limit }
        }, transport: transport);

        var result = await service.SendAsync(CreateRequest(body), CreateAccount(), new() { SendIntervalMs = 0 });

        Assert.True(result.IsComplete);
        Assert.Equal(body, string.Concat(transport.Messages.Select(text => text[1..^1].TrimStart('…'))));
        Assert.All(transport.Messages, text => Assert.True(text.Length <= limit));
        Assert.All(transport.Messages, text => Assert.DoesNotContain('�', text));
    }

    /// <summary>上限缺失时只强制刷新一次，仍未知则不发请求。</summary>
    [Fact]
    public async Task MissingLimitRefreshesOnceAndDoesNotSend()
    {
        var config = new FakeBilibiliDanmuConfigService { Config = new() };
        var transport = new ScriptedDanmuSendTransport();
        var service = new DanmuSendService(danmuConfigService: config, transport: transport);

        var result = await service.SendAsync(CreateRequest("正文"), CreateAccount(), new());

        Assert.False(result.IsComplete);
        Assert.Equal(2, config.GetRequests.Count);
        Assert.Empty(transport.Messages);
        Assert.Contains("上限", result.Results.Single().ErrorMessage);
    }

    /// <summary>限频按一秒、两秒退避且只发布一个分段终态。</summary>
    [Fact]
    public async Task RetryOnlyRateLimitsAndPublishesOneFinalRecordPerSegment()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited, ApiCode = 123 });
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited, ApiCode = 123 });
        var clock = new ManualSendTimeProvider();
        var service = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService(),
            transport: transport, timeProvider: clock);
        var records = new List<DanmuFeedItem>();
        service.RecordCreated += (_, record) => records.Add(record);

        var sending = service.SendAsync(CreateRequest("正文"), CreateAccount(), new() { SendIntervalMs = 0 });
        await clock.WaitForTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        await clock.WaitForTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(2));
        var result = await sending;

        Assert.True(result.IsComplete);
        Assert.Equal(3, transport.Messages.Count);
        Assert.Single(records);
        Assert.Equal(3, result.Results.Single().AttemptCount);
    }

    /// <summary>非限频失败既不重试，也不发送后续分段。</summary>
    [Theory]
    [InlineData(DanmuSendFailureKind.Blocked)]
    [InlineData(DanmuSendFailureKind.Rejected)]
    [InlineData(DanmuSendFailureKind.OutcomeUnknown)]
    public async Task OtherFailuresStopWithoutRetryOrLaterSegments(DanmuSendFailureKind failure)
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = failure, ApiCode = failure == DanmuSendFailureKind.Rejected ? 10030 : null });
        var service = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService
        {
            Config = new() { Length = 6 }
        }, transport: transport);

        var result = await service.SendAsync(CreateRequest("甲乙丙丁戊己庚辛"), CreateAccount(), new());

        Assert.False(result.IsComplete);
        Assert.Single(transport.Messages);
    }

    /// <summary>上限改变后只重分未成功正文，并复用原失败记录标识。</summary>
    [Fact]
    public async Task ContinuationResendsOnlyUnacceptedBodyAndReusesFailedRecordIdentity()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        var config = new FakeBilibiliDanmuConfigService { Config = new() { Length = 6 } };
        var service = new DanmuSendService(danmuConfigService: config, transport: transport);
        var request = CreateRequest("甲乙丙丁戊己庚辛壬癸");
        var settings = new AppSettings { SendIntervalMs = 0 };
        var first = await service.SendAsync(request, CreateAccount(), settings);
        var failedId = first.Results.Last().Record.SendRecordId;
        config.Config = new() { Length = 5 };

        var second = await service.SendAsync(request, CreateAccount(), settings, first.Progress);

        Assert.True(second.IsComplete);
        Assert.Equal("【甲乙丙丁】", transport.Messages[0]);
        Assert.DoesNotContain(transport.Messages.Skip(1), text => text.Contains("甲乙"));
        Assert.Equal(failedId, second.Results[1].Record.SendRecordId);
        Assert.Equal(10, second.Progress.GetAcceptedLength(0));
    }

    /// <summary>凭据变化使重发从新操作起点开始。</summary>
    [Fact]
    public async Task CredentialChangeStartsFreshDeliveryInsteadOfContinuingOldIdentity()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        var service = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService
        {
            Config = new() { Length = 6 }
        }, transport: transport);
        var request = CreateRequest("甲乙丙丁戊己");
        var settings = new AppSettings { SendIntervalMs = 0 };
        var first = await service.SendAsync(request, CreateAccount(), settings);
        var changed = CreateAccount();
        changed.Cookie = "SESSDATA=other;bili_jct=other;DedeUserID=43";

        var second = await service.SendAsync(request, changed, settings, first.Progress);

        Assert.True(second.IsComplete);
        Assert.NotEqual(first.Progress.OperationId, second.Progress.OperationId);
        Assert.Equal("【甲乙丙丁】", transport.Messages[2]);
    }

    /// <summary>取消退避会阻止后续请求且不制造拒绝记录。</summary>
    [Fact]
    public async Task CancelledBackoffNeverStartsAnotherAttempt()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited });
        var clock = new ManualSendTimeProvider();
        var service = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService(),
            transport: transport, timeProvider: clock);
        using var cancellation = new CancellationTokenSource();
        var records = new List<DanmuFeedItem>();
        service.RecordCreated += (_, record) => records.Add(record);
        var sending = service.SendAsync(CreateRequest("正文"), CreateAccount(), new(), cancellationToken: cancellation.Token);
        await clock.WaitForTimerAsync();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Single(transport.Messages);
        Assert.Empty(records);
    }

    /// <summary>第三次限频后终止当前消息，后续逻辑消息按序发送。</summary>
    [Fact]
    public async Task RateLimitRetriesAreBoundedAndKeepLaterMessagesQueued()
    {
        var transport = new ScriptedDanmuSendTransport();
        for (var index = 0; index < 3; index++)
        {
            transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited });
        }
        var clock = new ManualSendTimeProvider();
        var service = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService(),
            transport: transport, timeProvider: clock);
        var settings = new AppSettings { SendIntervalMs = 0 };
        var first = service.SendAsync(CreateRequest("先发"), CreateAccount(), settings);
        await clock.WaitForTimerAsync();
        var second = service.SendAsync(CreateRequest("后发"), CreateAccount(), settings);
        Assert.Single(transport.Messages);
        clock.Advance(TimeSpan.FromSeconds(1));
        await clock.WaitForTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.False((await first).IsComplete);
        Assert.True((await second).IsComplete);
        Assert.Equal(["【先发】", "【先发】", "【先发】", "【后发】"], transport.Messages);
    }

    /// <summary>关闭限频重试后第一次拒绝即终止。</summary>
    [Fact]
    public async Task DisablingRetryMakesRateLimitTerminal()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited });
        var service = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService(), transport: transport);
        var result = await service.SendAsync(CreateRequest("正文"), CreateAccount(), new() { RetryWhenRateLimited = false });
        Assert.False(result.IsComplete);
        Assert.Single(transport.Messages);
    }

    /// <summary>预算不足以容纳一个完整文字元素时不发送请求。</summary>
    [Fact]
    public async Task TextElementLargerThanRemainingBudgetFailsWithoutRequest()
    {
        var transport = new ScriptedDanmuSendTransport();
        var service = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService
        {
            Config = new() { Length = 4 }
        }, transport: transport);
        var result = await service.SendAsync(CreateRequest("👩‍💻"), CreateAccount(), new());
        Assert.False(result.IsComplete);
        Assert.Empty(transport.Messages);
    }

    /// <summary>单次 HTTP 边界正确识别 429 和明确频率业务响应。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualHttpTransportRetriesOnlyExplicitFrequencyEvidence(bool businessResponse)
    {
        using var handler = new DanmuSendHttpHandler { ResponseJson = "{\"code\":0,\"data\":{}}" };
        handler.ResponseStatuses.Enqueue(businessResponse ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests);
        handler.ResponseBodies.Enqueue(businessResponse ? "{\"code\":999,\"message\":\"发送频率过快\"}" : "{}");
        using var client = new HttpClient(handler);
        var clock = new ManualSendTimeProvider();
        var service = new DanmuSendService(client, new FakeBilibiliDanmuConfigService(), timeProvider: clock);
        var sending = service.SendAsync(CreateRequest("正文"), CreateAccount(), new() { SendIntervalMs = 0 });
        await clock.WaitForTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await sending;
        Assert.True(result.IsComplete);
        Assert.Equal(2, handler.RequestCount);
        Assert.Single(result.Results);
    }

    /// <summary>取消期间已确认成功的正文仍记入进度，续发不重复。</summary>
    [Fact]
    public async Task ConfirmedSuccessDuringCancellationRemainsAvailableForContinuation()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new ScriptedDanmuSendTransport { BeforeResponse = cancellation.Cancel };
        var service = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService
        {
            Config = new() { Length = 6 }
        }, transport: transport);
        var request = CreateRequest("甲乙丙丁戊己");
        var account = CreateAccount();
        var progress = new DanmuDeliveryProgress(request, account);
        var settings = new AppSettings { SendIntervalMs = 0 };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SendAsync(request, account, settings, progress, cancellation.Token));
        Assert.Equal(4, progress.GetAcceptedLength(0));

        var continued = await service.SendAsync(request, account, settings, progress);

        Assert.True(continued.IsComplete);
        Assert.Equal(["【甲乙丙丁】", "【…戊己】"], transport.Messages);
    }

    /// <summary>取消排队中间项不会让后一项越过尚未完成的请求。</summary>
    [Fact]
    public async Task CancellingQueuedMessageDoesNotLetLaterMessageOvertakeCurrentOne()
    {
        var clock = new ManualSendTimeProvider();
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.RateLimited });
        var service = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService(),
            transport: transport, timeProvider: clock);
        var settings = new AppSettings { SendIntervalMs = 0 };
        var first = service.SendAsync(CreateRequest("先发"), CreateAccount(), settings);
        await clock.WaitForTimerAsync();
        using var cancellation = new CancellationTokenSource();
        var cancelled = service.SendAsync(CreateRequest("取消"), CreateAccount(), settings, cancellationToken: cancellation.Token);
        var last = service.SendAsync(CreateRequest("后发"), CreateAccount(), settings);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Single(transport.Messages);

        clock.Advance(TimeSpan.FromSeconds(1));
        await first;
        await last;

        Assert.Equal(["【先发】", "【先发】", "【后发】"], transport.Messages);
    }

    /// <summary>较大发送间隔计算退避时不会发生整数溢出。</summary>
    [Fact]
    public void RetryDelayDoesNotOverflowLargeValidIntervals()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(2L * int.MaxValue),
            DanmuSendRetryPolicy.GetDelay(DanmuSendFailureKind.RateLimited, true, 2, int.MaxValue));
    }

    /// <summary>进队前已取消的请求不会占住后续发送的队列位置。</summary>
    [Fact]
    public async Task AlreadyCancelledRequestDoesNotBlockNextDelivery()
    {
        var transport = new ScriptedDanmuSendTransport();
        var service = new DanmuSendService(danmuConfigService: new FakeBilibiliDanmuConfigService(), transport: transport);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SendAsync(CreateRequest("取消"),
            CreateAccount(), new(), cancellationToken: cancellation.Token));

        var next = await service.SendAsync(CreateRequest("后发"), CreateAccount(), new());

        Assert.True(next.IsComplete);
        Assert.Equal(["【后发】"], transport.Messages);
    }

    private static DanmuSendRequest CreateRequest(string body)
    {
        return new() { RoomId = "100", Contents = [new() { Language = "original", Body = body, Prefix = "【", Suffix = "】" }] };
    }

    private static BilibiliAccount CreateAccount()
    {
        return new() { Id = "account", Cookie = "SESSDATA=test;bili_jct=test;DedeUserID=42" };
    }
}
