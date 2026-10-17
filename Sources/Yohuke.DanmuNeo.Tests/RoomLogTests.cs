using System.Threading;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Models.Logging;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证房间业务错误进入会话日志且保留取消、来源和发送结果边界。
/// </summary>
public class RoomLogTests
{
    /// <summary>
    /// 同传接口拒绝会写入错误，并保留原来的草稿恢复与反馈。
    /// </summary>
    [AvaloniaFact]
    public async Task DraftRejectionLogsOnceAndRestoresDraft()
    {
        using var log = new AppLogService();
        using var room = CreateRoom(log, new FakeDanmuSendService { IsAccepted = false });
        room.InputDraft = "同传内容";

        await room.SendDraftAsync();

        var entry = Assert.Single(log.Entries);
        Assert.Equal(AppLogLevel.ERROR, entry.Level);
        Assert.Contains("同传发送", entry.Source);
        Assert.Contains("测试发送失败", entry.Message);
        Assert.Contains("接口状态码 -1", entry.Message);
        Assert.True(room.IsDraftSendFailed);
        Assert.Equal("同传内容", room.InputDraft);
    }

    /// <summary>
    /// 每次主动发送失败独立记录，不按持续连接故障去重。
    /// </summary>
    [AvaloniaFact]
    public async Task RepeatedDraftRejectionsAreEachLogged()
    {
        using var log = new AppLogService();
        using var room = CreateRoom(log, new FakeDanmuSendService { IsAccepted = false });
        room.InputDraft = "同传内容";

        await room.SendDraftAsync();
        await room.SendDraftAsync();

        Assert.Equal(2, log.Entries.Count);
    }

    /// <summary>
    /// 真实发送服务转为失败结果后，日志仍保留传输异常与内部异常。
    /// </summary>
    [AvaloniaFact]
    public async Task TransportFailureResultPreservesExceptionDetails()
    {
        using var handler = new DanmuSendHttpHandler
        {
            Exception = new HttpRequestException("传输失败", new IOException("内部网络错误"))
        };
        using var client = new HttpClient(handler);
        var sender = new DanmuSendService(client, new FakeBilibiliDanmuConfigService());
        using var log = new AppLogService();
        using var room = CreateRoom(log, sender);
        room.InputDraft = "同传内容";

        await room.SendDraftAsync();

        var entry = Assert.Single(log.Entries);
        Assert.Contains(nameof(HttpRequestException), entry.Details);
        Assert.Contains(nameof(IOException), entry.Details);
        Assert.Contains("内部网络错误", entry.Details);
        Assert.True(room.IsDraftSendFailed);
    }

    /// <summary>
    /// 分段发送部分失败在公共发送边界合为一条日志。
    /// </summary>
    [AvaloniaFact]
    public async Task PartialDraftRejectionLogsAcceptedPartCountOnce()
    {
        var sender = new FakeDanmuSendService { MessageParts = ["第一段", "第二段"] };
        sender.AcceptanceResults.Enqueue(true);
        sender.AcceptanceResults.Enqueue(false);
        using var log = new AppLogService();
        using var room = CreateRoom(log, sender);
        room.InputDraft = "同传内容";

        await room.SendDraftAsync();

        Assert.Contains("1/2 段接口已接受", Assert.Single(log.Entries).Message);
        Assert.Contains("部分发送失败", room.DraftSendStatus);
    }

    /// <summary>
    /// 旧请求错误仍归属原房间，且不能用旧草稿覆盖后续输入。
    /// </summary>
    [AvaloniaFact]
    public async Task DelayedDraftExceptionKeepsOriginalSourceAndNewDraft()
    {
        var sender = new BlockingDanmuSendService { Exception = new IOException("请求失败") };
        using var log = new AppLogService();
        using var room = CreateRoom(log, sender);
        room.InputDraft = "旧草稿";
        var sending = room.SendDraftAsync();
        await sender.FirstSendStarted;
        room.RoomId = "999";
        room.RoomName = "新直播间";
        room.InputDraft = "新草稿";
        sender.ReleaseFirstSend();

        await sending;

        var entry = Assert.Single(log.Entries);
        Assert.Contains("原工作区", entry.Source);
        Assert.Contains("原直播间", entry.Source);
        Assert.Contains("房间 200", entry.Source);
        Assert.DoesNotContain("新直播间", entry.Source);
        Assert.Contains(nameof(IOException), entry.Details);
        Assert.Equal("新草稿", room.InputDraft);
        Assert.False(room.IsDraftSendFailed);
    }

    /// <summary>
    /// 歌词发送异常不会在公共发送与歌词层重复记录。
    /// </summary>
    [AvaloniaFact]
    public async Task LyricExceptionLogsOnceAndDoesNotMarkLineSent()
    {
        var sender = new BlockingDanmuSendService { Exception = new IOException("歌词请求失败") };
        using var log = new AppLogService();
        using var room = CreateRoom(log, sender);
        room.LyricInput = "[00:01]歌词";
        room.ApplyLyric();
        var sending = room.SendCurrentLyricAsync();
        await sender.FirstSendStarted;
        sender.ReleaseFirstSend();

        await sending;

        Assert.Contains("歌词发送", Assert.Single(log.Entries).Source);
        Assert.False(room.Lyrics[0].IsSent);
    }

    /// <summary>
    /// 监听重连期间相同错误只记一次，恢复后再次失败可以重新记录。
    /// </summary>
    [AvaloniaFact]
    public async Task ListeningDiagnosticsDeduplicateUntilRecovered()
    {
        var factory = new FakeLiveDanmuSocketFactory();
        factory.Socket.KeepRunning = true;
        using var log = new AppLogService();
        using var room = CreateRoom(log, socketFactory: factory);
        room.StartListening();
        await factory.Socket.StartedTask;
        var error = new IOException("监听连接失败", new InvalidOperationException("底层诊断"));

        factory.Socket.EmitError(error);
        factory.Socket.EmitDisconnected();
        factory.Socket.EmitError(error);
        Assert.Single(log.Entries);
        factory.Socket.EmitRecovered();
        factory.Socket.EmitError(error);

        Assert.Equal(2, log.Entries.Count);
        Assert.All(log.Entries, entry => Assert.Contains("底层诊断", entry.Details));
        room.StopListening();
        factory.Socket.EmitError(new IOException("停止后的迟到错误"));
        Assert.Equal(2, log.Entries.Count);
        await factory.Socket.DisposedTask;
    }

    /// <summary>
    /// 转发接口拒绝会显示失败并只记录一次，不再误报已转发。
    /// </summary>
    [AvaloniaFact]
    public async Task ForwardRejectionDoesNotReportSuccessOrDuplicateLog()
    {
        var source = CreateSource();
        var factory = new FakeLiveDanmuSocketFactory();
        factory.Socket.KeepRunning = true;
        factory.Socket.NextMessage = new() { RoomId = source.RoomId, Uid = 42, Content = "转发内容" };
        using var log = new AppLogService();
        using var room = CreateRoom(log, new FakeDanmuSendService { IsAccepted = false }, factory,
            forwardingSource: source);

        room.RestartForwarding();
        await WaitForAsync(() => room.ForwardRules[0].StatusText.StartsWith("转发失败：", StringComparison.Ordinal));

        var entry = Assert.Single(log.Entries);
        Assert.Contains("来源工作区", entry.Source);
        Assert.Contains("来源直播间", entry.Source);
        Assert.Contains("房间 200", entry.Source);
        Assert.Contains("测试发送失败", entry.Message);
        Assert.DoesNotContain("已转发", room.ForwardRules[0].StatusText);
        room.StopForwarding();
        await factory.Socket.DisposedTask;
    }

    /// <summary>
    /// 转发连接的重复异常也在恢复后重置去重状态。
    /// </summary>
    [AvaloniaFact]
    public async Task ForwardDiagnosticsDeduplicateUntilRecovered()
    {
        var factory = new FakeLiveDanmuSocketFactory();
        factory.Socket.KeepRunning = true;
        using var log = new AppLogService();
        using var room = CreateRoom(log, socketFactory: factory, forwardingSource: CreateSource());
        room.RestartForwarding();
        await factory.Socket.StartedTask;
        var error = new IOException("来源监听失败");

        factory.Socket.EmitError(error);
        factory.Socket.EmitError(error);
        Assert.Single(log.Entries);
        factory.Socket.EmitRecovered();
        factory.Socket.EmitError(error);

        Assert.Equal(2, log.Entries.Count);
        room.StopForwarding();
        await factory.Socket.DisposedTask;
    }

    /// <summary>
    /// 转发来源缺失时在保留规则状态的同时输出可定位的错误。
    /// </summary>
    [AvaloniaFact]
    public void ForwardValidationFailureIsLogged()
    {
        using var log = new AppLogService();
        using var room = CreateRoom(log);
        room.ForwardRules.Add(new(new() { Id = "missing-source", IsEnabled = true }, _ => { }));

        room.RestartForwarding();

        var entry = Assert.Single(log.Entries);
        Assert.Contains("missing-source", entry.Source);
        Assert.Equal("请选择源直播间", entry.Message);
        Assert.Equal(entry.Message, room.ForwardRules[0].StatusText);
    }

    /// <summary>
    /// 播放地址获取失败保留完整异常详情。
    /// </summary>
    [AvaloniaFact]
    public async Task PlaybackFailureLogsExceptionDetails()
    {
        var streams = new DeferredLiveStreamService();
        using var log = new AppLogService();
        using var room = CreateRoom(log, streams: streams);
        var playing = room.PlayLiveAsync();
        streams.Requests[0].SetException(new IOException("直播取流失败"));

        await playing;

        var entry = Assert.Single(log.Entries);
        Assert.Contains("播放器", entry.Source);
        Assert.Contains("直播取流失败", entry.Details);
        Assert.False(room.IsLivePlayerVisible);
    }

    /// <summary>
    /// 停止播放后迟到的取流异常属于取消操作，不记为新的故障。
    /// </summary>
    [AvaloniaFact]
    public async Task CanceledPlaybackIgnoresLateException()
    {
        var streams = new DeferredLiveStreamService();
        using var log = new AppLogService();
        using var room = CreateRoom(log, streams: streams);
        var playing = room.PlayLiveAsync();
        room.StopLivePlayer();
        streams.Requests[0].SetException(new IOException("取消后的异常"));

        await playing;

        Assert.Empty(log.Entries);
    }

    /// <summary>
    /// 弹幕配置读取失败在日志中携带接口异常而不丢失行内反馈。
    /// </summary>
    [AvaloniaFact]
    public async Task DanmuConfigurationReadFailureIsLogged()
    {
        var config = new FakeBilibiliDanmuConfigService { Exception = new IOException("配置读取失败") };
        using var log = new AppLogService();
        using var room = CreateRoom(log, configuration: config);

        await room.ActivateDanmuConfigurationAsync();

        var entry = Assert.Single(log.Entries);
        Assert.Contains("弹幕配置读取", entry.Source);
        Assert.Contains("配置读取失败", entry.Details);
        Assert.Contains("配置读取失败", room.DanmuConfigurationStatus);
    }

    /// <summary>
    /// 保存失败与恢复读取失败各保留一条不同来源的诊断。
    /// </summary>
    [AvaloniaFact]
    public async Task DanmuConfigurationSaveAndRecoveryFailuresAreLogged()
    {
        var config = new FakeBilibiliDanmuConfigService();
        using var log = new AppLogService();
        using var room = CreateRoom(log, configuration: config);
        await room.ActivateDanmuConfigurationAsync();
        config.WriteAsync = (_, _, _, _) => Task.FromException<DanmuRoomConfig>(new IOException("保存请求失败"));
        config.Exception = new IOException("恢复请求失败");

        room.SelectedDanmuColor = room.DanmuColorOptions[1];
        await room.DanmuConfigurationTask;

        Assert.Equal(2, log.Entries.Count);
        Assert.Contains(log.Entries, entry => entry.Source.Contains("弹幕配置保存") && entry.Details.Contains("保存请求失败"));
        Assert.Contains(log.Entries, entry => entry.Source.Contains("弹幕配置恢复") && entry.Details.Contains("恢复请求失败"));
    }

    /// <summary>
    /// 禁言接口异常进入日志，账号 Cookie 不出现在来源或摘要中。
    /// </summary>
    [AvaloniaFact]
    public async Task ModerationFailureIsLogged()
    {
        var moderation = new FakeBilibiliModerationService { MuteException = new IOException("禁言请求失败") };
        using var log = new AppLogService();
        using var room = CreateRoom(log, moderation: moderation);
        room.AddDanmu(new() { RoomId = "200", Uid = 30, UserName = "目标用户", Content = "远端弹幕" });
        var item = Assert.Single(room.DanmuItems);
        Assert.True(await room.RefreshModerationPermissionAsync(item));

        await room.MuteDanmuAsync(item);

        var entry = Assert.Single(log.Entries);
        Assert.Contains("禁言", entry.Source);
        Assert.Contains("禁言请求失败", entry.Details);
        Assert.Contains("禁言失败", room.ModerationStatus);
    }

    /// <summary>
    /// 用户名查询失败保留规则上下文与异常详情。
    /// </summary>
    [AvaloniaFact]
    public async Task ForwardUserNameFailureIsLogged()
    {
        var names = new FakeBilibiliUserNameService
        {
            RespondAsync = (_, _) => Task.FromException<string>(new IOException("用户名请求失败"))
        };
        using var log = new AppLogService();
        using var rule = new DanmuForwardRuleViewModel(new() { Id = "rule-name", SenderUid = "42" }, _ => { });
        rule.ConfigureUserNameLookup(names, () => null, TimeSpan.Zero, log);

        await rule.UserNameLookupTask;

        var entry = Assert.Single(log.Entries);
        Assert.Contains("rule-name", entry.Source);
        Assert.Contains("UID 42", entry.Message);
        Assert.Contains("用户名请求失败", entry.Details);
    }

    /// <summary>
    /// 没有头像缓存的正常空结果不伪造错误或重复服务已记录的故障。
    /// </summary>
    [AvaloniaFact]
    public async Task EmptyAvatarResultDoesNotCreateError()
    {
        using var log = new AppLogService();
        using var room = CreateRoom(log);

        Assert.False(await room.LoadAvatarAsync(force: true));

        Assert.Empty(log.Entries);
    }

    private static LiveRoomTabViewModel CreateRoom(
        IAppLogService log,
        IDanmuSendService? sender = null,
        ILiveDanmuSocketFactory? socketFactory = null,
        IBilibiliLiveStreamService? streams = null,
        IBilibiliDanmuConfigService? configuration = null,
        IBilibiliModerationService? moderation = null,
        ForwardSourceRoomOption? forwardingSource = null)
    {
        var account = new BilibiliAccount { Id = "account", Cookie = "test-account" };
        var state = new LiveRoomTabState { Id = "room-state", RoomId = "200", RoomName = "原直播间" };

        if (forwardingSource is not null)
        {
            state.ForwardRules.Add(new()
            {
                Id = "forward-rule",
                IsEnabled = true,
                SourceWorkspaceId = forwardingSource.WorkspaceId,
                SourceRoomStateId = forwardingSource.RoomStateId,
                SenderUid = "42"
            });
        }

        var room = new LiveRoomTabViewModel(state);
        room.Configure(_ => account, (_, _) => account, () => new(), () => new(), () => [],
            _ => forwardingSource is null ? [] : [forwardingSource], sender ?? new FakeDanmuSendService(),
            new FakeRoomAvatarService(), () => Task.CompletedTask, streams, new FakeLivePlayerService(),
            liveDanmuSocketFactory: socketFactory,
            moderationService: moderation,
            danmuConfigService: configuration ?? new FakeBilibiliDanmuConfigService(),
            log: log, logWorkspace: "原工作区");
        return room;
    }

    private static ForwardSourceRoomOption CreateSource()
    {
        return new()
        {
            WorkspaceId = "source-workspace",
            WorkspaceName = "来源工作区",
            RoomStateId = "source-room",
            RoomId = "100",
            RoomName = "来源直播间"
        };
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
