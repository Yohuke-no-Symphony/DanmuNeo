using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证消息会话来源和后台监听结束后的清理顺序。
/// </summary>
public class LiveRoomFeedSocketTests
{
    /// <summary>
    /// 监听连接的大航海事件进入共享付费列表。
    /// </summary>
    [AvaloniaFact]
    public void GuardSocketEventReachesPaidFeed()
    {
        var socket = new FakeLiveDanmuSocket { KeepRunning = true };
        using var context = new RoomModerationTestContext(new SequenceLiveDanmuSocketFactory(socket));
        context.Room.StartListening();
        socket.EmitGuardPurchase(CreatePurchase());
        Dispatcher.UIThread.RunJobs();

        var item = Assert.Single(context.Room.SuperChats);
        Assert.Equal(LivePaidEventKind.GuardPurchase, item.Kind);
        Assert.Equal("上舰用户", item.UserName);
        Assert.Equal("舰长", item.BadgeText);
    }

    /// <summary>
    /// 切换房间后丢弃旧连接已经排入 UI 的弹幕和上舰事件。
    /// </summary>
    [AvaloniaFact]
    public void ChangingRoomDiscardsQueuedOldMessages()
    {
        var socket = new FakeLiveDanmuSocket { KeepRunning = true };
        using var context = new RoomModerationTestContext(new SequenceLiveDanmuSocketFactory(socket));
        context.Room.StartListening();
        socket.EmitDanmu(new() { RoomId = "100", Uid = 42, Content = "旧房间消息" });
        socket.EmitGuardPurchase(CreatePurchase());
        context.Room.RoomId = "101";
        Dispatcher.UIThread.RunJobs();

        Assert.Single(context.Room.DanmuItems);
        Assert.Empty(context.Room.SuperChats);
        Assert.False(context.Room.IsListening);
    }

    /// <summary>
    /// 停止并立即重启时，旧连接及其排队消息不会污染新会话。
    /// </summary>
    [AvaloniaFact]
    public void RestartDiscardsOldSessionAndKeepsNewSession()
    {
        var oldSocket = new FakeLiveDanmuSocket { KeepRunning = true };
        var newSocket = new FakeLiveDanmuSocket { KeepRunning = true };
        using var context = new RoomModerationTestContext(new SequenceLiveDanmuSocketFactory(oldSocket, newSocket));
        context.Room.StartListening();
        oldSocket.EmitGuardPurchase(CreatePurchase());
        context.Room.StopListening();
        context.Room.StartListening();
        oldSocket.EmitDanmu(new() { RoomId = "100", Uid = 42, Content = "旧连接" });
        oldSocket.EmitDisconnected();
        newSocket.EmitGuardPurchase(CreatePurchase());
        Dispatcher.UIThread.RunJobs();

        Assert.Single(context.Room.SuperChats);
        Assert.Single(context.Room.DanmuItems);
        Assert.True(context.Room.IsListening);
        Assert.Equal("监听中", context.Room.ConnectionStatus);
    }

    /// <summary>
    /// 后台已结束而 UI 尚未清理时，修改房间号仍能安全取消监听。
    /// </summary>
    [AvaloniaFact]
    public void FinishedSocketCanBeStoppedBeforeUiCleanup()
    {
        var socket = new FakeLiveDanmuSocket();
        using var context = new RoomModerationTestContext(new SequenceLiveDanmuSocketFactory(socket));
        context.Room.StartListening();
        Assert.True(socket.DisposedTask.Wait(TimeSpan.FromSeconds(5)));

        context.Room.RoomId = "101";
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.Room.IsListening);
    }

    private static BilibiliGuardPurchaseMessage CreatePurchase()
    {
        return new()
            { RoomId = "100", Uid = 42, UserName = "上舰用户", GuardLevel = 3, Timestamp = 1800000000, Quantity = 1 };
    }
}
