using Avalonia.Controls;
using Avalonia.Controls.Recycling;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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
}
