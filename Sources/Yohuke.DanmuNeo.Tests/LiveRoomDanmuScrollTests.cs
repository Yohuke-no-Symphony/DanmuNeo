using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 弹幕翻阅、阅读锚点和排队自动滚动的真实界面回归测试。
/// </summary>
public class LiveRoomDanmuScrollTests
{
    /// <summary>
    /// 用户翻阅发生在排队回顶之前时，旧回调不能把阅读位置拉回。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrollOffsetAfterQueuedCallbackDoesNotReturnToLatest(bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, compact);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            context.Room.AddDanmu(new() { RoomId = "100", Uid = 30, Content = "排队中的最新弹幕" });
            scroll.Offset = new Vector(0, 180);
            var offset = scroll.Offset.Y;
            Assert.True(offset > 1);
            PumpLayout(window);

            Assert.True(scroll.Offset.Y > 1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 翻阅时插入不同高度的新弹幕并裁剪旧记录，仍保留正在阅读的条目位置。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncomingDanmuPreservesReadingAnchor(bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, compact);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            WheelDown(window, scroll);
            PumpLayout(window);
            var anchor = view.GetVisualDescendants().OfType<Button>()
                .Where(control => control.Classes.Contains("danmuFeedButton"))
                .First(control => control.TranslatePoint(default, scroll)!.Value.Y >= 0);
            var anchorItem = anchor.DataContext;
            var before = anchor.TranslatePoint(default, scroll)!.Value.Y;
            context.Room.AddDanmu(new()
            {
                RoomId = "100", Uid = 30, UserName = "新用户", MedalName = "长牌子", MedalLevel = 30,
                Content = new string('长', 180)
            });
            PumpLayout(window);

            Assert.Same(anchorItem, anchor.DataContext);
            Assert.InRange(anchor.TranslatePoint(default, scroll)!.Value.Y, before - 0.5, before + 0.5);
            Assert.Equal(300, context.Room.DanmuItems.Count);
            Assert.True(scroll.Offset.Y > 1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 回到顶部后恢复跟随，清空列表也复位到最新位置。
    /// </summary>
    [AvaloniaFact]
    public void ReturningToLatestAndResetResumeFollowing()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, false);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            WheelDown(window, scroll);
            PumpLayout(window);
            scroll.Offset = default;
            PumpLayout(window);
            context.Room.AddDanmu(new() { RoomId = "100", Uid = 30, Content = "恢复跟随" });
            PumpLayout(window);
            Assert.Equal(0, scroll.Offset.Y);
            WheelDown(window, scroll);
            PumpLayout(window);
            context.Room.DanmuItems.Clear();
            context.Room.AddDanmu(new() { RoomId = "100", Uid = 30, Content = "重置后新消息" });
            PumpLayout(window);

            Assert.Equal(0, scroll.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 切换房间时废弃旧回调与旧集合监听，重新挂载后恢复新房间的跟随。
    /// </summary>
    [AvaloniaFact]
    public void SwitchingRoomAndReattachingIgnoresPreviousSession()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context, false);
        var window = CreateWindow(context, view);
        var nextRoom = new LiveRoomTabViewModel(new() { RoomId = "200" });

        for (var index = 0; index < 300; index++)
        {
            nextRoom.DanmuItems.Add(new() { RoomId = "200", UserName = "新房间用户", Content = $"新房间历史 {index}" });
        }

        try
        {
            ShowWindow(window);
            var scroll = view.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            context.Room.AddDanmu(new() { RoomId = "100", Content = "旧房间排队回调" });
            view.DataContext = nextRoom;
            scroll.Offset = new Vector(0, 180);
            context.Room.DanmuItems.Clear();
            context.Room.AddDanmu(new() { RoomId = "100", Content = "旧房间重置事件" });
            PumpLayout(window);
            Assert.True(scroll.Offset.Y > 1);
            nextRoom.AddDanmu(new() { RoomId = "200", Content = "新房间阅读中新消息" });
            PumpLayout(window);
            Assert.True(scroll.Offset.Y > 1);
            window.Content = null;
            PumpLayout(window);
            window.Content = view;
            PumpLayout(window);
            Assert.Equal(0, scroll.Offset.Y);
            nextRoom.AddDanmu(new() { RoomId = "200", Content = "重新挂载后的新消息" });
            PumpLayout(window);
            Assert.Equal(0, scroll.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }

    private static LiveRoomWorkspaceView CreateView(LiveRoomFeedViewTestContext context, bool compact)
    {
        context.ViewModel.IsCompactDanmuDisplay = compact;
        context.Room.DanmuItems.Clear();

        for (var index = 0; index < 300; index++)
        {
            context.Room.DanmuItems.Add(new DanmuFeedItem
            {
                RoomId = "100", UserUid = "30", UserName = $"用户 {index}", Content = $"历史弹幕 {index}"
            });
        }

        return new() { DataContext = context.Room };
    }

    private static Window CreateWindow(LiveRoomFeedViewTestContext context, LiveRoomWorkspaceView view)
    {
        return new() { Width = 1100, Height = 800, DataContext = context.ViewModel, Content = view };
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
        PumpLayout(window);
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void WheelDown(Window window, ScrollViewer scroll)
    {
        var point = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseWheel(point, new Vector(0, -6));
    }
}
