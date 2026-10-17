using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证窗口菜单与标题共用一行，并保留独立的菜单点击区域。
/// </summary>
public class WorkbenchTitleBarTests
{
    /// <summary>
    /// 标题区域的双击切换最大化与还原，连续点击按每两个点击切换一次。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TitleBarDoubleClickTogglesMaximizedState(bool emptyArea)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        context.ViewModel.Settings.UseWindowMenuOnMac = true;
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var title = main.GetVisualDescendants().OfType<TitleBarView>().Single();
            var name = title.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "夜半同传");
            var menu = main.FindControl<Menu>("WindowMenu")!;
            var buttons = title.FindControl<StackPanel>("WindowButtons")!;
            var point = emptyArea
                ? new Point((GetBounds(menu, main).Right + GetBounds(buttons, main).Left) / 2,
                    GetBounds(title, main).Center.Y)
                : GetBounds(name, main).Center;
            Assert.True(title.IsDragRegion(point));

            Click(main, point);
            Assert.Equal(WindowState.Normal, main.WindowState);
            Click(main, point);
            Assert.Equal(WindowState.Maximized, main.WindowState);
            Click(main, point);
            Assert.Equal(WindowState.Maximized, main.WindowState);
            Click(main, point);
            Assert.Equal(WindowState.Normal, main.WindowState);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 不允许最大化或调整尺寸时保持原状态，全屏窗口也不会被标题栏双击带出全屏。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, true, WindowState.Normal)]
    [InlineData(true, false, WindowState.Normal)]
    [InlineData(true, true, WindowState.FullScreen)]
    public async Task TitleBarDoubleClickRespectsWindowCapabilitiesAndFullScreen(
        bool canResize, bool canMaximize, WindowState state)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            main.CanResize = canResize;
            main.CanMaximize = canMaximize;
            main.WindowState = state;
            PumpLayout(main);
            var title = main.GetVisualDescendants().OfType<TitleBarView>().Single();
            var name = title.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "夜半同传");
            var point = GetBounds(name, main).Center;

            Click(main, point);
            Click(main, point);

            Assert.Equal(state, main.WindowState);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 菜单、置顶按钮、原生按钮保留区的双击和标题区域右击不触发窗口最大化。
    /// </summary>
    [AvaloniaFact]
    public async Task DoubleClickOutsideDragRegionAndRightClickKeepWindowState()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        context.ViewModel.Settings.UseWindowMenuOnMac = true;
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var title = main.GetVisualDescendants().OfType<TitleBarView>().Single();
            title.CaptionInsets = new(92, 0, 146, 0);
            PumpLayout(main);
            var pin = title.FindControl<Button>("PinButton")!;
            var menu = main.FindControl<Menu>("WindowMenu")!;
            var item = menu.Items.OfType<MenuItem>().First();
            var bounds = GetBounds(title, main);
            var points = new[]
            {
                GetBounds(pin, main).Center,
                GetBounds(item, main).Center,
                new Point(bounds.Left + 46, bounds.Center.Y),
                new Point(bounds.Right - 73, bounds.Center.Y)
            };

            foreach (var point in points)
            {
                Assert.False(title.IsDragRegion(point));
                Click(main, point);
                Click(main, point);
                Assert.Equal(WindowState.Normal, main.WindowState);
                item.IsSubMenuOpen = false;
                PumpLayout(main);
            }

            var name = title.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "夜半同传");
            var titlePoint = GetBounds(name, main).Center;

            for (var i = 0; i < 2; i++)
            {
                main.MouseDown(titlePoint, MouseButton.Right);
                main.MouseUp(titlePoint, MouseButton.Right);
            }

            Assert.Equal(WindowState.Normal, main.WindowState);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 工作区名称不显示在标题中，应用名、菜单及置顶操作避开原生按钮保留区。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(980, 92, 0)]
    [InlineData(980, 0, 146)]
    [InlineData(980, 92, 146)]
    [InlineData(1280, 92, 0)]
    [InlineData(1280, 0, 146)]
    [InlineData(1280, 92, 146)]
    public async Task MenuSharesTitleRowAndStillOpensFromPointer(double width, double leftInset, double rightInset)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        context.ViewModel.Settings.WindowWidth = width;
        context.ViewModel.Settings.UseWindowMenuOnMac = true;
        var name = string.Concat(Enumerable.Repeat("很长的工作区名称", 8));
        context.ViewModel.SelectedWorkspace!.Name = name;
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var title = main.GetVisualDescendants().OfType<TitleBarView>().Single();
            title.CaptionInsets = new(leftInset, 0, rightInset, 0);
            PumpLayout(main);
            var menu = main.FindControl<Menu>("WindowMenu")!;
            var buttons = title.FindControl<StackPanel>("WindowButtons")!;
            var titleBounds = GetBounds(title, main);
            var menuBounds = GetBounds(menu, main);
            var buttonsBounds = GetBounds(buttons, main);
            var applicationTitle = title.FindControl<TextBlock>("ApplicationTitleText")!;
            var nameBounds = GetBounds(applicationTitle, main);
            Assert.DoesNotContain(title.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == name || text.Text == "默认工作区");
            Assert.Equal(SystemDecorations.Full, main.SystemDecorations);
            Assert.Equal(42, titleBounds.Height);
            Assert.InRange(menuBounds.Top, titleBounds.Top, titleBounds.Bottom);
            Assert.InRange(menuBounds.Bottom, titleBounds.Top, titleBounds.Bottom);
            Assert.True(nameBounds.Left >= titleBounds.Left + leftInset);
            Assert.True(nameBounds.Right < menuBounds.Left);
            Assert.True(menuBounds.Right < buttonsBounds.Left);
            Assert.True(buttonsBounds.Right <= titleBounds.Right - rightInset);
            Assert.All(buttons.Children.OfType<Button>(), button =>
            {
                Assert.Contains(button.Name, new[] { "DebugToolsButton", "PinButton" });

                if (button.IsEffectivelyVisible)
                {
                    Assert.False(title.IsDragRegion(GetBounds(button, main).Center));
                }
            });
            Assert.True(title.IsDragRegion(nameBounds.Center));
            Assert.False(title.IsDragRegion(menuBounds.Center));

            if (leftInset > 0)
            {
                Assert.False(title.IsDragRegion(new(titleBounds.Left + leftInset / 2, titleBounds.Center.Y)));
            }

            if (rightInset > 0)
            {
                Assert.False(title.IsDragRegion(new(titleBounds.Right - rightInset / 2, titleBounds.Center.Y)));
            }

            var listen = title.FindControl<Button>("ListenToggleButton")!;
            var toolbarTop = listen.TranslatePoint(default, main)!.Value.Y;
            Assert.InRange(toolbarTop, titleBounds.Top, titleBounds.Bottom - listen.Bounds.Height);
            Assert.False(title.IsDragRegion(GetBounds(listen, main).Center));
            var views = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "视图"));
            var point = GetBounds(views, main).Center;
            Assert.False(title.IsDragRegion(point));
            var position = main.Position;
            Click(main, point);
            Assert.True(views.IsSubMenuOpen);
            Assert.Equal(position, main.Position);
            views.IsSubMenuOpen = false;
            PumpLayout(main);
            var pin = title.FindControl<Button>("PinButton")!;
            var pinPoint = GetBounds(pin, main).Center;
            Assert.False(title.IsDragRegion(pinPoint));
            Assert.False(main.Topmost);
            Click(main, pinPoint);
            Assert.True(main.Topmost);
            Assert.Contains("selected", pin.Classes);
            Assert.Equal(position, main.Position);
            Click(main, pinPoint);
            Assert.False(main.Topmost);
            Assert.DoesNotContain("selected", pin.Classes);

            title.Height = 56;
            PumpLayout(main);
            Assert.Equal(56, title.Bounds.Height);
            Assert.Equal(toolbarTop + 7, listen.TranslatePoint(default, main)!.Value.Y);
            Assert.InRange(GetBounds(menu, main).Bottom, 0, GetBounds(title, main).Bottom);

            foreach (var windowMode in new[] { false, true })
            {
                context.ViewModel.UseWindowMenuOnMac = windowMode;
                PumpLayout(main);
                Assert.Equal(56, title.Bounds.Height);
                Assert.Equal(toolbarTop + 7, listen.TranslatePoint(default, main)!.Value.Y);
                Assert.Equal(windowMode || !OperatingSystem.IsMacOS(), menu.IsVisible);
                Assert.True(title.IsDragRegion(GetBounds(applicationTitle, main).Center));
                Assert.False(title.IsDragRegion(GetBounds(pin, main).Center));
            }
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 原生按钮中心在最小标题行的上下两侧时，文字、菜单和操作按钮仍与其中心对齐。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(19, false)]
    [InlineData(19, true)]
    [InlineData(27, false)]
    [InlineData(27, true)]
    [InlineData(26.25, true)]
    public async Task CaptionContentFollowsMeasuredNativeButtonCenter(double center, bool windowMenu)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        context.ViewModel.Settings.WindowWidth = 980;
        context.ViewModel.Settings.UseWindowMenuOnMac = windowMenu;
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var title = main.GetVisualDescendants().OfType<TitleBarView>().Single();
            var caption = new Rect(14, center - 7, 52, 14);
            title.Height = MacOsCaptionGeometry.GetTitleBarHeight(42, caption);
            title.CaptionInsets = MacOsCaptionGeometry.GetCaptionInsets(caption, title.Height);
            PumpLayout(main);

            var content = title.GetVisualDescendants().OfType<Grid>().Single(grid => grid.Classes.Contains("titleBar"));
            var name = title.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "夜半同传");
            var pin = title.FindControl<Button>("PinButton")!;
            var menu = main.FindControl<Menu>("WindowMenu")!;
            var roundingTolerance = 0.5 / main.RenderScaling;
            Assert.InRange(Math.Abs(GetBounds(content, main).Center.Y - center), 0, roundingTolerance);
            Assert.InRange(Math.Abs(GetBounds(name, main).Center.Y - center), 0, roundingTolerance);
            Assert.InRange(Math.Abs(GetBounds(pin, main).Center.Y - center), 0, roundingTolerance);
            Assert.Equal(32, pin.Bounds.Height);

            if (menu.IsVisible)
            {
                Assert.InRange(Math.Abs(GetBounds(menu, main).Center.Y - center), 0, roundingTolerance);
            }

            var listen = title.FindControl<Button>("ListenToggleButton")!;
            Assert.InRange(Math.Abs(GetBounds(listen, main).Center.Y - center), 0, roundingTolerance);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 房间信息在菜单与右侧操作之间居中，切换房间、长文本和操作提示不会遮挡监听按钮。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(980, 92, 0)]
    [InlineData(980, 0, 146)]
    [InlineData(1280, 92, 0)]
    [InlineData(1280, 0, 146)]
    public async Task RoomHeaderStaysCenteredAndFollowsSelectedRoom(double width, double leftInset, double rightInset)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        context.ViewModel.Settings.WindowWidth = width;
        context.ViewModel.Settings.UseWindowMenuOnMac = true;
        context.ViewModel.SelectedWorkspace!.Name = new string('区', 50);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var title = main.GetVisualDescendants().OfType<TitleBarView>().Single();
            title.CaptionInsets = new(leftInset, 0, rightInset, 0);
            var region = title.FindControl<Grid>("RoomHeaderRegion")!;
            var header = title.FindControl<Grid>("RoomHeader")!;
            var roomName = title.FindControl<TextBlock>("CurrentRoomName")!;
            var roomId = title.FindControl<TextBlock>("CurrentRoomId")!;
            var listen = title.FindControl<Button>("ListenToggleButton")!;
            var buttons = title.FindControl<StackPanel>("WindowButtons")!;
            var menu = main.FindControl<Menu>("WindowMenu")!;
            var dock = main.FindControl<ContentControl>("DockHost")!;
            var workspace = context.ViewModel.SelectedWorkspace;
            var firstRoom = workspace.SelectedRoom!;
            var secondRoom = workspace.AddRoom("21685677", new string('铃', 80), "");
            var position = main.Position;

            foreach (var room in new[] { firstRoom, secondRoom, firstRoom })
            {
                workspace.SelectedRoom = room;
                context.ViewModel.StatusMessage = "已保存设置：" + new string('长', 100);
                PumpLayout(main);
                Assert.Same(room, header.DataContext);
                Assert.Same(room.ToggleListeningCommand, listen.Command);
                Assert.Equal(room.RoomName, roomName.Text);
                Assert.Equal(room.RoomId, roomId.Text);
                Assert.DoesNotContain(title.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == context.ViewModel.StatusMessage);
                var regionBounds = GetBounds(region, main);
                var headerBounds = GetBounds(header, main);
                var listenBounds = GetBounds(listen, main);
                Assert.InRange(Math.Abs(headerBounds.Center.X - regionBounds.Center.X), 0, 0.5);
                Assert.True(regionBounds.Left >= GetBounds(menu, main).Right);
                Assert.True(regionBounds.Right <= GetBounds(buttons, main).Left);
                Assert.True(headerBounds.Left >= regionBounds.Left);
                Assert.True(headerBounds.Right <= regionBounds.Right);
                Assert.True(GetBounds(roomName, main).Right <= GetBounds(roomId, main).Left);
                Assert.True(GetBounds(roomId, main).Right < listenBounds.Left);
                Assert.True(listenBounds.Right <= regionBounds.Right);
                Assert.InRange(GetBounds(dock, main).Top, title.Bounds.Bottom, title.Bounds.Bottom + 1);
                Assert.False(title.IsDragRegion(listenBounds.Center));
                Click(main, listenBounds.Center);
                Click(main, listenBounds.Center);
                Assert.Equal(WindowState.Normal, main.WindowState);
                Assert.Equal(position, main.Position);
            }

            context.ViewModel.StatusMessage = "";
            context.ViewModel.UseWindowMenuOnMac = false;
            PumpLayout(main);
            Assert.InRange(Math.Abs(GetBounds(header, main).Center.X - GetBounds(region, main).Center.X), 0, 0.5);
            workspace.SelectedRoom = null;
            PumpLayout(main);
            Assert.False(header.IsEffectivelyVisible);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    private static Rect GetBounds(Control control, Window window)
    {
        return new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
    }

    private static void Click(Window window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
