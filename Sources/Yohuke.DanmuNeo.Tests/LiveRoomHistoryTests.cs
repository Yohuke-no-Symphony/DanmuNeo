using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证历史补拉、监听会话隔离和归档在展示处理之前的行为。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class LiveRoomHistoryTests
{
    /// <summary>首次及重连补拉保留已有显示对象且失败不停止监听。</summary>
    [AvaloniaFact]
    public async Task InitialAndRecoveredHistoryMergeWithoutReplacingLiveObjects()
    {
        var socket = new FakeLiveDanmuSocket { KeepRunning = true };
        var history = new ControlledDanmuHistoryService();
        var account = new BilibiliAccount { Id = "account", Cookie = "fake" };
        using var room = CreateRoom(socket, history, () => account);
        room.StartListening();
        Assert.Equal(1, history.Count);
        socket.EmitDanmu(new() { RoomId = "100", Uid = 42, Content = "实时", Timestamp = 1800000001 });
        Dispatcher.UIThread.RunJobs();
        var live = Assert.Single(room.DanmuItems);
        history.Requests.Dequeue().SetResult([
            new() { RoomId = "100", Uid = 1, Content = "历史", Timestamp = 1800000000 },
            new() { RoomId = "100", Uid = 42, Content = "实时", Timestamp = 1800000001 }
        ]);
        await room.PendingHistoryTask;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, room.DanmuItems.Count);
        Assert.Same(live, room.DanmuItems[0]);
        Assert.Equal("历史", room.DanmuItems[1].Content);
        socket.EmitRecovered();
        Assert.Equal(2, history.Count);
        history.Requests.Dequeue().SetException(new IOException("history failed"));
        await room.PendingHistoryTask;
        Assert.True(room.IsListening);
    }

    /// <summary>账号 Cookie 改变后拒绝迟到历史结果。</summary>
    [AvaloniaFact]
    public async Task ChangedAccountCookieDiscardsLateHistory()
    {
        var socket = new FakeLiveDanmuSocket { KeepRunning = true };
        var history = new ControlledDanmuHistoryService();
        var account = new BilibiliAccount { Id = "account", Cookie = "first" };
        using var room = CreateRoom(socket, history, () => account);
        room.StartListening();
        account.Cookie = "second";
        history.Requests.Dequeue().SetResult([new() { RoomId = "100", Content = "迟到结果" }]);
        await room.PendingHistoryTask;
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(room.DanmuItems);
    }

    /// <summary>有效账号变化后启动新连接，旧请求和旧连接均不能写入新会话。</summary>
    [AvaloniaFact]
    public async Task AccountContextRefreshRestartsListenerAndRejectsOldSocket()
    {
        var oldSocket = new FakeLiveDanmuSocket { KeepRunning = true };
        var newSocket = new FakeLiveDanmuSocket { KeepRunning = true };
        var history = new ControlledDanmuHistoryService();
        var account = new BilibiliAccount { Id = "account", Cookie = "first" };
        using var room = CreateRoom(oldSocket, history, () => account,
            factory: new SequenceLiveDanmuSocketFactory(oldSocket, newSocket));
        room.StartListening();
        var oldTask = room.PendingHistoryTask;
        var oldRequest = history.Requests.Dequeue();
        account.Cookie = "second";

        room.NotifyDanmuConfigurationAccountChanged();

        Assert.True(room.IsListening);
        Assert.Equal(2, history.Count);
        oldSocket.EmitDanmu(new() { RoomId = "100", Content = "旧连接" });
        newSocket.EmitDanmu(new() { RoomId = "100", Content = "新连接" });
        oldRequest.SetResult([new() { RoomId = "100", Content = "旧历史" }]);
        history.Requests.Dequeue().SetResult([]);
        await Task.WhenAll(oldTask, room.PendingHistoryTask);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("新连接", Assert.Single(room.DanmuItems).Content);
    }

    /// <summary>立即重启可以发起新补拉，旧请求不会污染新会话。</summary>
    [AvaloniaFact]
    public async Task RestartStartsNewHistoryWithoutWaitingForOldRequest()
    {
        var oldSocket = new FakeLiveDanmuSocket { KeepRunning = true };
        var newSocket = new FakeLiveDanmuSocket { KeepRunning = true };
        var history = new ControlledDanmuHistoryService();
        var account = new BilibiliAccount { Id = "account", Cookie = "fake" };
        using var room = CreateRoom(oldSocket, history, () => account,
            factory: new SequenceLiveDanmuSocketFactory(oldSocket, newSocket));
        room.StartListening();
        var oldTask = room.PendingHistoryTask;
        var oldRequest = history.Requests.Dequeue();
        room.StopListening();
        room.StartListening();
        Assert.Equal(2, history.Count);
        oldRequest.SetResult([new() { RoomId = "100", Content = "旧会话" }]);
        history.Requests.Dequeue().SetResult([new() { RoomId = "100", Content = "新会话" }]);
        await Task.WhenAll(oldTask, room.PendingHistoryTask);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("新会话", Assert.Single(room.DanmuItems).Content);
    }

    /// <summary>隐藏弹幕和礼物聚合之前保存收到的原始观察记录。</summary>
    [AvaloniaFact]
    public void ArchivesHiddenDanmuAndRawGiftUpdatesBeforeDisplayAggregation()
    {
        var socket = new FakeLiveDanmuSocket { KeepRunning = true };
        var history = new ControlledDanmuHistoryService();
        var archive = new RecordingDanmuArchiveService();
        var settings = new AppSettings { HideEmoticonDanmu = true };
        using var room = CreateRoom(socket, history, () => new() { Id = "account", Cookie = "fake" }, settings, archive);
        room.StartListening();
        socket.EmitDanmu(new() { RoomId = "100", Uid = 42, Content = "[dog]", IsEmoticon = true });
        socket.EmitGift(new() { RoomId = "100", Uid = 42, UserName = "用户", GiftName = "礼物", Quantity = 1, BatchComboId = "combo" });
        socket.EmitGift(new() { RoomId = "100", Uid = 42, UserName = "用户", GiftName = "礼物", Quantity = 2, BatchComboId = "combo", BatchComboCount = 3 });
        socket.EmitSuperChat(new() { RoomId = "100", Uid = 42, UserName = "用户", Content = "SC", Timestamp = 1800000000 });
        socket.EmitGuardPurchase(new() { RoomId = "100", Uid = 42, UserName = "用户", GuardLevel = 3, Quantity = 1 });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, archive.Entries.Count);
        Assert.Contains(archive.Entries, entry => entry.Content == "[dog]");
        Assert.Single(room.DanmuItems);
        socket.EmitRecovered();
        Assert.Single(archive.Sessions);
        settings.ArchiveReceivedDanmu = false;
        room.RefreshDanmuArchiving();
        Assert.True(archive.Sessions[0].IsClosed);
        settings.ArchiveReceivedDanmu = true;
        room.RefreshDanmuArchiving();
        Assert.Equal(2, archive.Sessions.Count);
    }

    private static LiveRoomTabViewModel CreateRoom(FakeLiveDanmuSocket socket,
        IBilibiliDanmuHistoryService history, Func<BilibiliAccount> account,
        AppSettings? settings = null, IReceivedDanmuArchiveService? archive = null, ILiveDanmuSocketFactory? factory = null)
    {
        var room = new LiveRoomTabViewModel(new() { RoomId = "100", RoomName = "房间" });
        room.Configure(_ => account(), (_, _) => account(), () => settings ?? new(), () => new(), () => [], _ => [],
            new FakeDanmuSendService(), new FakeRoomAvatarService(), () => Task.CompletedTask,
            liveDanmuSocketFactory: factory ?? new SequenceLiveDanmuSocketFactory(socket),
            receptionServices: new(history, archive));
        return room;
    }
}
