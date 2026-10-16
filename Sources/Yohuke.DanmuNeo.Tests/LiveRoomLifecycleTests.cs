using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证房间播放请求、工作台停用及最终释放的独立生命周期。
/// </summary>
public class LiveRoomLifecycleTests
{
    /// <summary>
    /// 停止、切房、改房号与释放均取消取流，迟到响应不会恢复播放或监听。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("stop")]
    [InlineData("deactivate")]
    [InlineData("room-id")]
    [InlineData("dispose")]
    public async Task InvalidationRejectsPendingSource(string action)
    {
        var streams = new DeferredLiveStreamService();
        var players = new DeferredLivePlayerService();
        var sockets = new FakeLiveDanmuSocketFactory();
        using var room = CreateRoom(streams, players, sockets: sockets, autoStartListening: true);
        var playing = room.PlayLiveAsync();
        var pending = Assert.Single(streams.Requests);

        Invalidate(room, action);
        Assert.True(Assert.Single(streams.CancellationTokens).IsCancellationRequested);
        pending.SetResult(CreateSource(80));
        await playing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(players.Requests);
        Assert.Empty(room.LiveQualityOptions);
        Assert.Empty(room.LivePlayerUrl);
        Assert.False(room.IsLivePlayerVisible);
        Assert.Empty(sockets.Requests);
    }

    /// <summary>
    /// 已开始创建的会话即使忽略取消而迟到返回，也由创建它的服务立即撤销。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("stop")]
    [InlineData("deactivate")]
    [InlineData("room-id")]
    [InlineData("dispose")]
    public async Task InvalidationRevokesLateCreatedSession(string action)
    {
        var players = new DeferredLivePlayerService();
        var sockets = new FakeLiveDanmuSocketFactory();
        using var room = CreateRoom(new FakeBilibiliLiveStreamService(), players,
            sockets: sockets, autoStartListening: true);
        var playing = room.PlayLiveAsync();
        var pending = Assert.Single(players.Requests);

        Invalidate(room, action);
        Assert.True(Assert.Single(players.CancellationTokens).IsCancellationRequested);
        pending.SetResult(CreateSession("obsolete"));
        await playing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["obsolete"], players.RevokedTokens);
        Assert.Empty(room.LivePlayerUrl);
        Assert.False(room.IsLivePlayerVisible);
        Assert.Empty(sockets.Requests);
    }

    /// <summary>
    /// 连续追帧逆序完成时只接受最新会话，旧播放与迟到会话分别撤销一次。
    /// </summary>
    [AvaloniaFact]
    public async Task ReversedChasesKeepLatestSession()
    {
        var players = new DeferredLivePlayerService();
        using var room = CreateRoom(new FakeBilibiliLiveStreamService(), players);
        var initial = room.PlayLiveAsync();
        players.Requests[0].SetResult(CreateSession("initial"));
        await initial;
        var older = room.ChaseLiveAsync();
        var newer = room.ChaseLiveAsync();

        Assert.True(players.CancellationTokens[1].IsCancellationRequested);
        Assert.Empty(players.RevokedTokens);
        Assert.EndsWith("initial", room.LivePlayerUrl);
        players.Requests[2].SetResult(CreateSession("latest"));
        await newer;
        players.Requests[1].SetResult(CreateSession("stale"));
        await older;

        Assert.True(room.IsLivePlayerVisible);
        Assert.EndsWith("latest", room.LivePlayerUrl);
        Assert.Equal(["initial", "stale"], players.RevokedTokens);
    }

    /// <summary>
    /// 旧取流晚于新请求返回时，不创建多余会话，也不覆盖新的清晰度选项。
    /// </summary>
    [AvaloniaFact]
    public async Task ReversedSourcesKeepLatestQualityAndSession()
    {
        var streams = new DeferredLiveStreamService();
        var players = new DeferredLivePlayerService();
        using var room = CreateRoom(streams, players);
        var older = room.PlayLiveAsync();
        var newer = room.ChaseLiveAsync();
        streams.Requests[1].SetResult(CreateSource(80));
        await players.FirstRequest.WaitAsync(TimeSpan.FromSeconds(5));
        players.Requests[0].SetResult(CreateSession("latest"));
        await newer;
        streams.Requests[0].SetResult(CreateSource(150));
        await older;

        Assert.Single(players.Requests);
        Assert.Equal(80, Assert.Single(room.LiveQualityOptions).Quality);
        Assert.EndsWith("latest", room.LivePlayerUrl);
    }

    /// <summary>
    /// 旧创建请求的异常不会清除后来成功的播放状态。
    /// </summary>
    [AvaloniaFact]
    public async Task StaleFailureDoesNotStopSuccessfulReplacement()
    {
        var players = new DeferredLivePlayerService();
        using var room = CreateRoom(new FakeBilibiliLiveStreamService(), players);
        var older = room.PlayLiveAsync();
        var newer = room.ChaseLiveAsync();
        players.Requests[1].SetResult(CreateSession("latest"));
        await newer;
        players.Requests[0].SetException(new IOException("旧请求失败"));
        await older;

        Assert.True(room.IsLivePlayerVisible);
        Assert.EndsWith("latest", room.LivePlayerUrl);
        Assert.Empty(players.RevokedTokens);
    }

    /// <summary>
    /// 清晰度单独刷新同样服从房间停用，不会在停用后回写选项。
    /// </summary>
    [AvaloniaFact]
    public async Task DeactivationRejectsPendingQualityRefresh()
    {
        var streams = new DeferredLiveStreamService();
        using var room = CreateRoom(streams, new FakeLivePlayerService());
        var refreshing = room.RefreshLiveQualitiesAsync();
        room.Deactivate();
        streams.Requests[0].SetResult(CreateSource(80));
        await refreshing;

        Assert.True(streams.CancellationTokens[0].IsCancellationRequested);
        Assert.Empty(room.LiveQualityOptions);
    }

    /// <summary>
    /// 工作台停用只停止播放与配置交互，原监听和歌词会话直到 Dispose 才结束。
    /// </summary>
    [AvaloniaFact]
    public async Task DeactivationPreservesListeningAndLyricsUntilDisposal()
    {
        var sockets = new FakeLiveDanmuSocketFactory();
        sockets.Socket.KeepRunning = true;
        var players = new FakeLivePlayerService();
        using var room = CreateRoom(new FakeBilibiliLiveStreamService(), players, sockets: sockets);
        room.LyricInput = "[00:00.00]第一句\n[01:00.00]第二句";
        room.ApplyLyric();
        room.StartListening();
        await sockets.Socket.StartedTask.WaitAsync(TimeSpan.FromSeconds(5));
        var lyrics = room.StartAutoLyricAsync();

        try
        {
            await room.PlayLiveAsync();
            await room.ActivateDanmuConfigurationAsync();
            Assert.True(room.CanSelectDanmuColor);
            room.Deactivate();

            Assert.True(room.IsListening);
            Assert.True(room.IsLyricAutoSending);
            Assert.False(sockets.Socket.DisposedTask.IsCompleted);
            Assert.False(room.CanSelectDanmuColor);
            Assert.False(room.IsLivePlayerVisible);
            Assert.Equal(["token-1"], players.RevokedTokens);
        }
        finally
        {
            room.Dispose();
            await lyrics.WaitAsync(TimeSpan.FromSeconds(5));
            await sockets.Socket.DisposedTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.False(room.IsListening);
        Assert.False(room.IsLyricAutoSending);
        room.StartListening();
        await room.StartAutoLyricAsync();
        await room.ActivateDanmuConfigurationAsync();
        Assert.Single(sockets.Requests);
        Assert.False(room.IsLyricAutoSending);
        Assert.False(room.CanSelectDanmuColor);
    }

    /// <summary>
    /// 转发面板隐藏与房间停用不重启连接，最终释放退订连接和发送记录。
    /// </summary>
    [AvaloniaFact]
    public async Task DisposalStopsForwardingAndUnsubscribesRecords()
    {
        var source = new ForwardSourceRoomOption
        {
            WorkspaceId = "source-workspace", RoomStateId = "source-room", RoomId = "200"
        };
        var state = new LiveRoomTabState
        {
            RoomId = "100",
            ForwardRules =
            [
                new()
                {
                    IsEnabled = true, SourceWorkspaceId = source.WorkspaceId,
                    SourceRoomStateId = source.RoomStateId, SenderUid = "42"
                }
            ]
        };
        var sends = new LifecycleDanmuSendService();
        var sockets = new FakeLiveDanmuSocketFactory();
        sockets.Socket.KeepRunning = true;
        using var room = CreateRoom(new FakeBilibiliLiveStreamService(), new FakeLivePlayerService(),
            sockets: sockets, sends: sends, state: state, source: source);
        room.RestartForwarding();
        await sockets.Socket.StartedTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, sends.SubscriberCount);

        room.Deactivate();
        Assert.False(sockets.Socket.DisposedTask.IsCompleted);
        room.Dispose();
        await sockets.Socket.DisposedTask.WaitAsync(TimeSpan.FromSeconds(5));
        sockets.Socket.EmitDanmu(new() { RoomId = "200", Uid = 42, Content = "已释放的消息" });
        room.RestartForwarding();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, sends.SubscriberCount);
        Assert.Empty(sends.SentMessages);
        Assert.Single(sockets.Requests);
    }

    /// <summary>
    /// 已提交发送在切房时继续完成，失败只恢复原房间草稿，不依赖面板存在。
    /// </summary>
    [AvaloniaFact]
    public async Task DeactivationPreservesSubmittedDraftFeedback()
    {
        var sends = new BlockingDanmuSendService { IsAccepted = false };
        using var room = CreateRoom(new FakeBilibiliLiveStreamService(), new FakeLivePlayerService(), sends: sends);
        room.InputDraft = "已提交的草稿";
        var sending = room.SendDraftAsync();
        await sends.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.Empty(room.InputDraft);
            room.Deactivate();
        }
        finally
        {
            sends.ReleaseFirstSend();
            await sending.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal("已提交的草稿", room.InputDraft);
        Assert.True(room.IsDraftSendFailed);
    }

    /// <summary>
    /// 最终释放不取消已提交发送，但迟到结果不再恢复已释放房间的草稿或历史。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisposalLeavesSubmittedSendRunningWithoutLateFeedback(bool accepted)
    {
        var sends = new BlockingDanmuSendService { IsAccepted = accepted };
        var records = new List<DanmuFeedItem>();
        sends.RecordCreated += (_, record) => records.Add(record);
        using var room = CreateRoom(new FakeBilibiliLiveStreamService(), new FakeLivePlayerService(), sends: sends);
        room.InputDraft = "已经提交";
        var sending = room.SendDraftAsync();
        await sends.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            room.Dispose();
            Assert.False(sending.IsCompleted);
        }
        finally
        {
            sends.ReleaseFirstSend();
            await sending.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(accepted, Assert.Single(records).IsSendAccepted);
        Assert.Empty(room.InputDraft);
        Assert.Empty(room.TranslateHistoryItems);
        Assert.False(room.IsDraftSendFailed);
    }

    /// <summary>
    /// 删除单个房间只释放该房间；工作区最终释放其余房间，重复释放安全。
    /// </summary>
    [AvaloniaFact]
    public async Task WorkspaceOwnsRoomRemovalAndDisposal()
    {
        using var workspace = new WorkspaceViewModel(new()
        {
            LiveRooms = [new() { RoomId = "100" }, new() { RoomId = "200" }]
        }, _ => "账号");
        var sends = new LifecycleDanmuSendService();
        var players = new FakeLivePlayerService();

        foreach (var room in workspace.Rooms)
        {
            ConfigureRoom(room, new FakeBilibiliLiveStreamService(), players, sends: sends);
            await room.PlayLiveAsync();
        }

        var removed = workspace.Rooms[0];
        var retained = workspace.Rooms[1];
        workspace.RemoveRoom(removed);
        Assert.False(removed.IsLivePlayerVisible);
        Assert.True(retained.IsLivePlayerVisible);
        Assert.Equal(1, sends.SubscriberCount);
        Assert.Equal(["token-1"], players.RevokedTokens);

        workspace.Dispose();
        workspace.Dispose();
        Assert.False(retained.IsLivePlayerVisible);
        Assert.Equal(0, sends.SubscriberCount);
        Assert.Equal(["token-1", "token-2"], players.RevokedTokens);
        Assert.Throws<ObjectDisposedException>(() => workspace.AddRoom("300", "已释放"));
    }

    /// <summary>
    /// 重新注入服务只保留一个发送记录订阅，最终释放时解除该订阅。
    /// </summary>
    [AvaloniaFact]
    public void ReconfigurationDoesNotDuplicateRecordSubscriptions()
    {
        var sends = new LifecycleDanmuSendService();
        using var room = CreateRoom(new FakeBilibiliLiveStreamService(), new FakeLivePlayerService(), sends: sends);
        ConfigureRoom(room, new FakeBilibiliLiveStreamService(), new FakeLivePlayerService(), sends: sends);
        Assert.Equal(1, sends.SubscriberCount);
        room.Dispose();
        Assert.Equal(0, sends.SubscriberCount);
    }

    private static void Invalidate(LiveRoomTabViewModel room, string action)
    {
        switch (action)
        {
            case "stop":
                room.StopLivePlayer();
                break;
            case "deactivate":
                room.Deactivate();
                break;
            case "room-id":
                room.RoomId = "200";
                break;
            case "dispose":
                room.Dispose();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private static LiveStreamPlaySource CreateSource(int quality)
    {
        return new()
        {
            RoomId = "100", CurrentQuality = quality, StreamUrl = "https://example.invalid/live.flv",
            QualityOptions = [new() { Quality = quality, Description = $"测试清晰度 {quality}" }]
        };
    }

    private static LivePlayerSession CreateSession(string token)
    {
        return new() { Token = token, PlayerUrl = $"http://127.0.0.1/player?token={token}" };
    }

    private static LiveRoomTabViewModel CreateRoom(
        IBilibiliLiveStreamService streams,
        ILivePlayerService players,
        FakeLiveDanmuSocketFactory? sockets = null,
        IDanmuSendService? sends = null,
        bool autoStartListening = false,
        LiveRoomTabState? state = null,
        ForwardSourceRoomOption? source = null)
    {
        var room = new LiveRoomTabViewModel(state ?? new() { RoomId = "100" });
        ConfigureRoom(room, streams, players, sockets, sends, autoStartListening, source);
        return room;
    }

    private static void ConfigureRoom(
        LiveRoomTabViewModel room,
        IBilibiliLiveStreamService streams,
        ILivePlayerService players,
        FakeLiveDanmuSocketFactory? sockets = null,
        IDanmuSendService? sends = null,
        bool autoStartListening = false,
        ForwardSourceRoomOption? source = null)
    {
        var settings = new AppSettings { AutoStartListeningWithLivePlayer = autoStartListening };
        var account = new BilibiliAccount { Cookie = "DedeUserID=42; bili_jct=test; SESSDATA=test" };
        room.Configure(_ => account, (_, _) => account, () => settings,
            () => MarkSymbolService.CreateDefaultGroup(settings), () => [],
            _ => source is null ? [] : [source], sends ?? new LifecycleDanmuSendService(),
            new FakeRoomAvatarService(), () => Task.CompletedTask, streams, players,
            liveDanmuSocketFactory: sockets ?? new FakeLiveDanmuSocketFactory(),
            danmuConfigService: new FakeBilibiliDanmuConfigService());
    }
}
