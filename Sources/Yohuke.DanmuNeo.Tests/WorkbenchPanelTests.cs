using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 独立面板复用和跨房间交互归属的回归测试。
/// </summary>
public class WorkbenchPanelTests
{
    /// <summary>
    /// 直接更改房间选择时，所有面板复用实例并同步数据上下文。
    /// </summary>
    [AvaloniaFact]
    public void AllPanelsReuseInstancesWhenSelectionChanges()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panels = new WorkbenchPanelSet(context);
        var original = panels.Panels.ToDictionary();
        var firstSelection = context.CaptureSelection();
        var workspace = fixture.ViewModel.SelectedWorkspace!;
        var second = workspace.AddRoom("200", "第二直播间");

        Assert.False(context.IsCurrent(firstSelection));
        Assert.Same(second, context.CurrentRoom);
        Assert.Equal(7, panels.Panels.Count);

        foreach (var pair in panels.Panels)
        {
            Assert.Same(original[pair.Key], pair.Value);
            Assert.Same(second, pair.Value.DataContext);
        }

        workspace.SelectedRoom = fixture.Room;
        Assert.Same(fixture.Room, context.CurrentRoom);
        Assert.All(panels.Panels.Values, panel => Assert.Same(fixture.Room, panel.DataContext));
    }

    /// <summary>
    /// 切房后迟到的插入聚焦不能覆盖新房间草稿。
    /// </summary>
    [AvaloniaFact]
    public void DelayedFeedInsertionCannotCrossRoomSelection()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panels = new WorkbenchPanelSet(context);
        var interactions = new WorkbenchInteractionCoordinator(context, panels.Composer);
        var activated = new List<string>();
        interactions.SetPanelActivator(activated.Add);
        panels.Composer.Input.Text = "旧草稿";
        panels.Composer.Input.CaretIndex = 1;
        interactions.InsertFeedContent(fixture.Room, context.CaptureSelection()!, "弹幕");
        var workspace = fixture.ViewModel.SelectedWorkspace!;
        var second = workspace.AddRoom("200", "第二直播间");
        second.InputDraft = "新房间草稿";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([WorkbenchPanelIds.COMPOSER], activated);
        Assert.Equal("新房间草稿", panels.Composer.Input.Text);
        Assert.Equal("新房间草稿", second.InputDraft);
        Assert.Contains("弹幕", fixture.Room.InputDraft);
    }

    /// <summary>
    /// 按下旧消息后切房或更改房间身份，迟到的点击不能写入新草稿。
    /// 同时覆盖回收后的按钮，以及显式恢复旧消息上下文以模拟仍持有来源的迟到回调。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(WorkbenchPanelIds.DANMU, false)]
    [InlineData(WorkbenchPanelIds.DANMU, true)]
    [InlineData(WorkbenchPanelIds.SUPER_CHAT, false)]
    [InlineData(WorkbenchPanelIds.SUPER_CHAT, true)]
    public void PressedFeedCannotInsertAfterRoomSelectionOrIdentityChanges(string panelId, bool changeIdentity)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panels = new WorkbenchPanelSet(context);
        var panel = panels.Panels[panelId];
        var window = new Window { Width = 500, Height = 500, Content = panel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var button = FindMessageButton(panel);
            var content = button.GetVisualDescendants().OfType<Control>()
                .Single(control => control.Classes.Contains("regularFeedContent"));
            var point = content.TranslatePoint(new Point(content.Bounds.Width / 2, content.Bounds.Height / 2), window)!
                .Value;
            var item = button.DataContext;
            fixture.Room.InputDraft = "旧房间草稿";
            window.MouseDown(point, MouseButton.Left);

            if (changeIdentity)
            {
                fixture.Room.RoomId = "200";
            }
            else
            {
                fixture.ViewModel.SelectedWorkspace!.AddRoom("200", "第二直播间");
            }

            var currentRoom = context.CurrentRoom!;
            currentRoom.InputDraft = "新房间草稿";
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("新房间草稿", currentRoom.InputDraft);
            button.DataContext = item;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("新房间草稿", currentRoom.InputDraft);
            Assert.Equal("新房间草稿", panels.Composer.Input.Text);

            if (!changeIdentity)
            {
                Assert.Equal("旧房间草稿", fixture.Room.InputDraft);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 迟到点击的消息已离开当前列表时，不能通过当前房间令牌插入。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(WorkbenchPanelIds.DANMU)]
    [InlineData(WorkbenchPanelIds.SUPER_CHAT)]
    public void RemovedFeedCannotInsertIntoCurrentDraft(string panelId)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panels = new WorkbenchPanelSet(context);
        var panel = panels.Panels[panelId];
        var window = new Window { Width = 500, Height = 500, Content = panel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var button = FindMessageButton(panel);
            fixture.Room.InputDraft = "保留草稿";

            if (button.DataContext is DanmuFeedItem danmu)
            {
                fixture.Room.DanmuItems.Remove(danmu);
            }
            else
            {
                fixture.Room.SuperChats.Remove(Assert.IsType<SuperChatItem>(button.DataContext));
            }

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("保留草稿", fixture.Room.InputDraft);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 同一个房间实例修改直播间号也会使旧交互代次失效。
    /// </summary>
    [AvaloniaFact]
    public async Task RoomIdentityChangeInvalidatesSelectionTokenWithoutDuplicateConfigurationLoad()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        await fixture.Room.DanmuConfigurationTask;
        Assert.Single(fixture.DanmuConfigService.GetRequests);
        var original = context.CaptureSelection();
        var notifications = 0;
        context.SelectionChanged += (_, _) => notifications++;
        fixture.Room.RoomId = "200";
        await fixture.Room.DanmuConfigurationTask;

        Assert.False(context.IsCurrent(original));
        Assert.True(context.IsCurrent(context.CaptureSelection()));
        Assert.Equal(1, notifications);
        Assert.Equal(2, fixture.DanmuConfigService.GetRequests.Count);
        Assert.Equal("200", fixture.DanmuConfigService.GetRequests[1].RoomId);
    }

    /// <summary>
    /// 普通视觉树卸载不改变房间的播放意图。
    /// </summary>
    [AvaloniaFact]
    public void DetachingPlayerDoesNotStopRoomPlaybackIntent()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new LivePlayerPanelView(context);
        var window = new Window { Content = panel };

        try
        {
            window.Show();
            fixture.Room.IsLivePlayerVisible = true;
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Room.IsLivePlayerVisible);
            window.Content = panel;
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Room.IsLivePlayerVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 歌词库选择器在切房时要求宿主关闭，避免选择结果落入另一房间。
    /// </summary>
    [AvaloniaFact]
    public void PickerRequestsCloseWhenCurrentRoomChanges()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var picker = new LyricLibraryPickerView(context);
        fixture.ViewModel.OpenLyricLibraryPicker();
        var closeRequests = 0;
        picker.CloseRequested += (_, _) => closeRequests++;
        fixture.ViewModel.SelectedWorkspace!.AddRoom("200", "第二直播间");

        Assert.Equal(1, closeRequests);
        Assert.False(fixture.ViewModel.IsLyricLibraryPickerOpen);
    }

    /// <summary>
    /// 输入和追帧工具条在窄面板中换行，各操作保持在面板内部。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(WorkbenchPanelIds.COMPOSER)]
    [InlineData(WorkbenchPanelIds.LIVE_PLAYER)]
    public void NarrowPanelToolbarsWrapWithoutHorizontalOverflow(string panelId)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panels = new WorkbenchPanelSet(context);
        var panel = panels.Panels[panelId];
        var window = new Window { Width = 220, Height = 400, Content = panel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var toolbar = panel.GetVisualDescendants().OfType<WrapPanel>().Single();
            Assert.True(toolbar.Bounds.Height > toolbar.Children.Max(control => control.Bounds.Height) + 4);

            foreach (var control in toolbar.Children)
            {
                Assert.InRange(control.Bounds.Right, 0, toolbar.Bounds.Width + 0.5);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static Button FindMessageButton(Control panel)
    {
        return panel.GetVisualDescendants().OfType<Button>().First(button =>
            button.Classes.Contains("danmuFeedButton") ||
            button.Classes.Contains("scFeedButton") && button.DataContext is SuperChatItem { CanInsertContent: true });
    }
}
