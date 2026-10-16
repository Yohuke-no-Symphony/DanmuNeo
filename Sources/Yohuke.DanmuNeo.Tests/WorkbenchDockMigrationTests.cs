using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Recycling;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model;
using Dock.Model.Core;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证 Dock 拖拽使用的移动和分割路径仍能呈现并编辑原来的业务控件。
/// </summary>
public class WorkbenchDockMigrationTests
{
    /// <summary>
    /// 标题拖动必须经过实际预览窗和停靠选择器，四向松手后的面板仍能编辑。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(DockOperation.Left)]
    [InlineData(DockOperation.Right)]
    [InlineData(DockOperation.Top)]
    [InlineData(DockOperation.Bottom)]
    public async Task PointerDraggingTitleThroughPreviewAndSelectorRetainsEditableViews(DockOperation operation)
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var composer = context.Controller.PanelAdapters[WorkbenchPanelIds.COMPOSER];
        var targetDock = Assert.IsAssignableFrom<IDock>(context.Controller.PanelAdapters[WorkbenchPanelIds.DANMU].Owner);
        var chrome = context.Owner.GetVisualDescendants().OfType<WorkbenchToolChromeControl>()
            .Single(control => ReferenceEquals(control.DataContext, composer.Owner));
        var title = chrome.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "PART_Title");
        var start = title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), context.Owner)!.Value;
        var target = context.Owner.GetVisualDescendants().OfType<ToolDockControl>()
            .Single(control => ReferenceEquals(control.DataContext, targetDock));
        var destination = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), context.Owner)!.Value;
        var pointerReleased = false;

        try
        {
            context.Owner.MouseMove(start);
            context.Owner.MouseDown(start, MouseButton.Left);
            context.Owner.MouseMove(destination, RawInputModifiers.LeftMouseButton);
            Pump(context);
            Assert.True(context.Controller.Host.IsDraggingDock);
            var adorner = Assert.Single(context.Owner.GetVisualDescendants().OfType<DockTarget>());
            var selector = adorner.GetVisualDescendants().OfType<Control>()
                .Single(control => control.Name == $"PART_{operation}Selector");
            Assert.True(selector.IsEffectivelyVisible);
            destination = context.Owner.PointToClient(GetSelectorScreenPoint(adorner, selector));
            context.Owner.MouseMove(destination, RawInputModifiers.LeftMouseButton);
            Pump(context);
            context.Owner.MouseUp(destination, MouseButton.Left);
            pointerReleased = true;
            Pump(context);

            Assert.False(context.Controller.Host.IsDraggingDock);
            Assert.Empty(context.Controller.FloatingWindows);
            Assert.Same(context.Owner, TopLevel.GetTopLevel(composer.View));
            Assert.NotSame(targetDock, composer.Owner);
            AssertAllPanelsRemainEditable(context);
        }
        finally
        {
            if (!pointerReleased)
            {
                context.Owner.MouseUp(destination, MouseButton.Left);
            }
        }
    }

    /// <summary>
    /// 真实输入在最小主窗和发送错误下经过主浮窗标题拖动，动态最小高度不能污染停靠测量。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(DockOperation.Left, false)]
    [InlineData(DockOperation.Right, false)]
    [InlineData(DockOperation.Top, false)]
    [InlineData(DockOperation.Bottom, false)]
    [InlineData(DockOperation.Left, true)]
    [InlineData(DockOperation.Right, true)]
    [InlineData(DockOperation.Top, true)]
    [InlineData(DockOperation.Bottom, true)]
    public async Task PointerDraggingRealComposerAtMinimumSizeRetainsRoomAndFiniteBounds(DockOperation operation, bool floating)
    {
        using var fixture = new WorkspaceSidebarTestContext(240);
        fixture.ViewModel.Settings.WindowWidth = 980;
        fixture.ViewModel.Settings.WindowHeight = 640;
        fixture.ViewModel.Settings.UseWindowMenuOnMac = true;

        if (floating)
        {
            await new WorkspaceLayoutStore(fixture.ViewModel.ConfigDirectory).SaveAsync(CreateFloatingComposerReproduction());
        }

        var main = new MainWindow { DataContext = fixture.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            var room = workbench.Context.CurrentRoom!;
            room.InputDraft = "拖动前草稿";
            room.DraftSendError = "发送失败：" + new string('错', 100);
            room.IsDraftSendFailed = true;
            var panel = workbench.Layouts.PanelAdapters[WorkbenchPanelIds.COMPOSER];
            var targetDock = Assert.IsAssignableFrom<IDock>(workbench.Layouts.PanelAdapters[WorkbenchPanelIds.DANMU].Owner);

            Pump(main, workbench.Layouts);
            var source = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(panel.View));
            var sourceHost = Assert.Single(source.GetVisualDescendants().OfType<DockControl>());
            var chrome = source.GetVisualDescendants().OfType<WorkbenchToolChromeControl>()
                .Single(control => ReferenceEquals(control.DataContext, panel.Owner));
            var title = chrome.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == "PART_Title");
            var start = title.TranslatePoint(new Point(title.Bounds.Width / 2, title.Bounds.Height / 2), source)!.Value;
            var target = main.GetVisualDescendants().OfType<ToolDockControl>()
                .Single(control => ReferenceEquals(control.DataContext, targetDock));
            var targetPoint = target.PointToScreen(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2));
            var destination = source.PointToClient(targetPoint);
            var pointerReleased = false;

            try
            {
                source.MouseMove(start);
                source.MouseDown(start, MouseButton.Left);
                source.MouseMove(start + new Vector(12, 0), RawInputModifiers.LeftMouseButton);
                Assert.True(sourceHost.IsDraggingDock);
                source.MouseMove(destination, RawInputModifiers.LeftMouseButton);
                Pump(main, workbench.Layouts);
                var adorner = Assert.Single(main.GetVisualDescendants().OfType<DockTarget>());
                var selector = adorner.GetVisualDescendants().OfType<Control>()
                    .Single(control => control.Name == $"PART_{operation}Selector");
                Assert.True(selector.IsEffectivelyVisible);
                destination = source.PointToClient(GetSelectorScreenPoint(adorner, selector));
                source.MouseMove(destination, RawInputModifiers.LeftMouseButton);
                Pump(main, workbench.Layouts);
                var manager = new DockManager(new DockService());
                var permitted = manager.ValidateDockable(panel, targetDock, DragAction.Move, operation, false);
                var selectedOperation = adorner.GetDockOperation(destination, target, source, DragAction.Move,
                    (_, requestedOperation, _, _) => manager.ValidateDockable(panel, targetDock, DragAction.Move, requestedOperation, false));
                var diagnostics = $"requested={operation}, manual={selectedOperation}, permitted={permitted}, " +
                                  $"selector={selector.Bounds}, enabled={selector.IsEnabled}, visible={selector.IsEffectivelyVisible}, opacity={selector.Opacity}, " +
                                  $"sourcePoint={destination}, sourcePosition={source.Position}, targetPosition={main.Position}, " +
                                  $"targetBounds={target.Bounds}; {GetDragDiagnostics(sourceHost)}";
                Assert.True(permitted, diagnostics);
                Assert.True(selectedOperation == operation, diagnostics);
                source.MouseUp(destination, MouseButton.Left);
                pointerReleased = true;
                Pump(main, workbench.Layouts);

                Assert.False(sourceHost.IsDraggingDock);
                Assert.True(workbench.Layouts.FloatingWindows.Count == 0, diagnostics);
                Assert.Same(main, TopLevel.GetTopLevel(panel.View));
                Assert.Same(room, workbench.Panels.Composer.DataContext);
                Assert.True(workbench.Panels.Composer.Input.Bounds.Height >= 64);
                Assert.True(workbench.Panels.Composer.Input.Focus());
                workbench.Panels.Composer.Input.CaretIndex = room.InputDraft.Length;
                main.KeyTextInput("继续输入");
                Assert.Equal("拖动前草稿继续输入", room.InputDraft);
                Assert.True(WorkspaceLayoutValidator.TryValidate(workbench.Layouts.Capture(), out var error), error);
            }
            finally
            {
                if (!pointerReleased && source.IsVisible)
                {
                    source.MouseUp(destination, MouseButton.Left);
                }
            }
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 单面板组被移除并折叠后，原视图必须进入目标组且其他标签仍可编辑。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(WorkbenchPanelIds.WORKSPACE)]
    [InlineData(WorkbenchPanelIds.LIVE_PLAYER)]
    [InlineData(WorkbenchPanelIds.COMPOSER)]
    [InlineData(WorkbenchPanelIds.SUPER_CHAT)]
    public async Task MovingLastPanelIntoAnotherGroupRetainsEditableViews(string panelId)
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var panel = context.Controller.PanelAdapters[panelId];
        var target = context.Controller.PanelAdapters[WorkbenchPanelIds.DANMU];
        var sourceDock = Assert.IsAssignableFrom<IDock>(panel.Owner);
        var targetDock = Assert.IsAssignableFrom<IDock>(target.Owner);
        Assert.Single(sourceDock.VisibleDockables!);
        var closed = new List<string>();
        context.Controller.PanelClosedByUser += closed.Add;

        context.Controller.RootDock.Factory!.MoveDockable(sourceDock, targetDock, panel, target);
        Pump(context);

        Assert.Same(targetDock, panel.Owner);
        AssertEditablePanel(context, panelId);
        AssertAllPanelsRemainEditable(context);
        Assert.Empty(closed);
    }

    /// <summary>
    /// 拖拽分割会重建工具组和空间容器，四个方向均不能遗失旧宿主持有的视图。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(DockOperation.Left)]
    [InlineData(DockOperation.Right)]
    [InlineData(DockOperation.Top)]
    [InlineData(DockOperation.Bottom)]
    public async Task DraggingPanelToSplitRetainsEditableViews(DockOperation operation)
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var panel = context.Controller.PanelAdapters[WorkbenchPanelIds.COMPOSER];
        var target = Assert.IsAssignableFrom<IDock>(context.Controller.PanelAdapters[WorkbenchPanelIds.DANMU].Owner);
        var manager = new DockManager(new DockService());

        Assert.True(manager.ValidateDockable(panel, target, DragAction.Move, operation, false));
        Assert.True(manager.ValidateDockable(panel, target, DragAction.Move, operation, true));
        Pump(context);

        Assert.NotSame(target, panel.Owner);
        AssertEditablePanel(context, WorkbenchPanelIds.COMPOSER);
        AssertAllPanelsRemainEditable(context);
    }

    /// <summary>
    /// 通过拖拽管理器反复浮出和停回主窗，窗口释放和切换标签都不能清空内容或触发业务关闭。
    /// </summary>
    [AvaloniaFact]
    public async Task DraggingBetweenMainAndFloatingHostsRetainsEditableViews()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var panel = context.Controller.PanelAdapters[WorkbenchPanelIds.COMPOSER];
        var target = context.Controller.PanelAdapters[WorkbenchPanelIds.DANMU];
        var manager = new DockManager(new DockService()) { ScreenPosition = new(200, 200) };
        var closed = new List<string>();
        context.Controller.PanelClosedByUser += closed.Add;

        for (var index = 0; index < 3; index++)
        {
            var targetDock = Assert.IsAssignableFrom<IDock>(target.Owner);
            Assert.True(manager.ValidateDockable(panel, targetDock, DragAction.Move, DockOperation.Window, true));
            Pump(context);
            Assert.Single(context.Controller.FloatingWindows);
            AssertEditablePanel(context, WorkbenchPanelIds.COMPOSER);

            Assert.True(manager.ValidateDockable(panel, targetDock, DragAction.Move, DockOperation.Fill, true));
            Pump(context);
            Assert.Empty(context.Controller.FloatingWindows);
            AssertEditablePanel(context, WorkbenchPanelIds.COMPOSER);
            AssertAllPanelsRemainEditable(context);
        }

        Assert.Empty(closed);
    }

    /// <summary>
    /// 整个标签组拖动并与另一组交换标签后，曾经呈现过的每个控件都可以重新获得焦点。
    /// </summary>
    [AvaloniaFact]
    public async Task DraggingTabGroupAndSwappingPanelsRetainsEditableViews()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        AssertAllPanelsRemainEditable(context);
        var danmu = context.Controller.PanelAdapters[WorkbenchPanelIds.DANMU];
        var player = context.Controller.PanelAdapters[WorkbenchPanelIds.LIVE_PLAYER];
        var sourceDock = Assert.IsAssignableFrom<IDock>(danmu.Owner);
        var targetDock = Assert.IsAssignableFrom<IDock>(player.Owner);
        var manager = new DockManager(new DockService());

        Assert.True(manager.ValidateDockable(sourceDock, targetDock, DragAction.Move, DockOperation.Left, true));
        Pump(context);
        AssertAllPanelsRemainEditable(context);

        context.Controller.RootDock.Factory!.SwapDockable(
            Assert.IsAssignableFrom<IDock>(danmu.Owner),
            Assert.IsAssignableFrom<IDock>(player.Owner), danmu, player);
        Pump(context);
        AssertAllPanelsRemainEditable(context);
    }

    /// <summary>
    /// 实际工作台输入面板迁移后保留原实例和房间上下文，键盘输入仍回写当前房间草稿。
    /// </summary>
    [AvaloniaFact]
    public async Task RealComposerRetainsRoomBindingAndKeyboardInputAfterDragging()
    {
        using var fixture = new WorkspaceSidebarTestContext(240);
        var window = new MainWindow { DataContext = fixture.ViewModel };

        try
        {
            window.Show();
            await window.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(window.Workbench);
            var composer = workbench.Panels.Composer;
            var room = workbench.Context.CurrentRoom!;
            room.InputDraft = "迁移前草稿";
            var panel = workbench.Layouts.PanelAdapters[WorkbenchPanelIds.COMPOSER];
            var target = workbench.Layouts.PanelAdapters[WorkbenchPanelIds.DANMU];
            var sibling = workbench.Layouts.PanelAdapters[WorkbenchPanelIds.HISTORY];
            var sourceDock = Assert.IsAssignableFrom<IDock>(panel.Owner);
            workbench.Layouts.RootDock.Factory!.MoveDockable(Assert.IsAssignableFrom<IDock>(sibling.Owner),
                sourceDock, sibling, panel);
            workbench.Layouts.ShowPanel(WorkbenchPanelIds.COMPOSER);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var formerHost = Assert.IsAssignableFrom<ContentControl>(composer.Parent);
            var recycling = ControlRecyclingDataTemplate.GetControlRecycling(formerHost)!;
            var manager = new DockManager(new DockService()) { ScreenPosition = new(200, 200) };

            foreach (var operation in new[] { DockOperation.Fill, DockOperation.Window, DockOperation.Bottom })
            {
                Assert.True(manager.ValidateDockable(panel, Assert.IsAssignableFrom<IDock>(target.Owner),
                    DragAction.Move, operation, true));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                foreach (var floating in workbench.Layouts.FloatingWindows)
                {
                    floating.UpdateLayout();
                }

                Dispatcher.UIThread.RunJobs();
                Assert.Same(composer, workbench.Panels.Composer);
                Assert.Same(composer, panel.View);
                Assert.Same(room, composer.DataContext);
                var host = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(composer));

                if (operation == DockOperation.Fill)
                {
                    Assert.Contains(sibling, sourceDock.VisibleDockables!);
                    var visualParent = composer.GetVisualParent();
                    Assert.Null(recycling.Build(panel, null, formerHost));
                    Assert.Same(visualParent, composer.GetVisualParent());
                    Assert.Same(host, TopLevel.GetTopLevel(composer));
                }

                host.Activate();
                Assert.True(composer.Input.Focus());
                composer.Input.CaretIndex = composer.Input.Text!.Length;
                var previousDraft = room.InputDraft;
                host.KeyTextInput("继续同传");
                Assert.Equal(previousDraft + "继续同传", room.InputDraft);
                Assert.Equal(room.InputDraft, composer.Input.Text);
            }
        }
        finally
        {
            await window.CloseWorkbenchAsync();
        }
    }

    private static void AssertAllPanelsRemainEditable(WorkbenchLayoutTestContext context)
    {
        foreach (var panelId in WorkbenchPanelIds.All)
        {
            context.Controller.ShowPanel(panelId);
            Pump(context);
            AssertEditablePanel(context, panelId);
        }

        Assert.True(WorkspaceLayoutValidator.TryValidate(context.Controller.Capture(), out var error), error);
    }

    private static void AssertEditablePanel(WorkbenchLayoutTestContext context, string panelId)
    {
        var panel = context.Controller.PanelAdapters[panelId];
        var view = Assert.IsType<TextBox>(context.Views[panelId]);
        var window = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(view));
        var hosts = context.Controller.FloatingWindows.Prepend(context.Owner).ToArray();
        Assert.Contains(window, hosts);
        Assert.Same(panel.Owner, Assert.Single(view.GetVisualAncestors().OfType<ToolControl>()).DataContext);
        Assert.True(view.IsEffectivelyVisible, panelId);
        Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0, panelId);
        Assert.Same(view, panel.View);
        window.Activate();
        Assert.True(view.Focus(), panelId);
        view.CaretIndex = view.Text?.Length ?? 0;
        var previous = view.Text;
        window.KeyTextInput("可输入");
        Assert.Equal(previous + "可输入", view.Text);
    }

    private static void Pump(WorkbenchLayoutTestContext context)
    {
        context.Pump();

        foreach (var window in context.Controller.FloatingWindows)
        {
            window.UpdateLayout();
        }

        Dispatcher.UIThread.RunJobs();
        context.Owner.UpdateLayout();
    }

    private static void Pump(Window main, WorkbenchLayoutController layouts)
    {
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();

        foreach (var window in layouts.FloatingWindows)
        {
            window.UpdateLayout();
        }

        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
    }

    private static string GetDragDiagnostics(DockControl host)
    {
        var state = typeof(DockControl).GetField("_dockControlState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var context = state.GetType().GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        var resolved = context.GetType().GetProperty("ResolvedOperation")!.GetValue(context);
        var targetPoint = context.GetType().GetProperty("TargetPoint")!.GetValue(context);
        var global = context.GetType().GetProperty("UseGlobalOperation")!.GetValue(context);
        var drop = state.GetType().BaseType!.GetProperty("DropControl", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state) as Control;
        var dockable = drop?.DataContext as IDockable;
        return $"resolved={resolved}, point={targetPoint}, global={global}, drop={drop?.GetType().Name}, dockable={dockable?.Title}/{dockable?.Id}";
    }

    private static PixelPoint GetSelectorScreenPoint(DockTarget adorner, Control selector)
    {
        var point = selector.TranslatePoint(new Point(selector.Bounds.Width / 2, selector.Bounds.Height / 2), adorner)!.Value;
        var adorned = AdornerLayer.GetAdornedElement(adorner);
        return (adorned ?? adorner).PointToScreen(point);
    }

    private static WorkspaceLayoutFile CreateFloatingComposerReproduction()
    {
        var file = WorkspaceLayoutPresets.CreateDefaultFile();
        var main = file.CurrentLayout.Main!;
        var center = main.Children[1];
        var right = main.Children[2];

        return file with
        {
            CurrentLayout = new()
            {
                Main = main with
                {
                    Children =
                    [
                        main.Children[0],
                        center.Children[0] with { Proportion = 0.4996825499311295 },
                        right with
                        {
                            Proportion = 0.3003174500688705,
                            Children =
                            [
                                right.Children[0] with { Proportion = 0.3137202483513328 },
                                right.Children[1] with { Proportion = 0.6862797516486673 }
                            ]
                        }
                    ]
                },
                FloatingWindows =
                [
                    new()
                    {
                        Layout = center.Children[1] with { Proportion = 0.16728642072392386 },
                        Bounds = new() { X = 600, Y = 544, Width = 478, Height = 180, Scaling = 2 }
                    }
                ],
                FocusedPanelId = WorkbenchPanelIds.COMPOSER
            }
        };
    }
}
