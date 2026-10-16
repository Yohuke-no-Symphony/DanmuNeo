using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证旧侧栏状态迁移为全局 Dock 后的隐藏恢复及退出保存。
/// </summary>
public class WorkspaceSidebarLayoutTests
{
    /// <summary>
    /// 旧折叠状态只迁移一次，移除顶部折叠按钮，菜单恢复面板不改变房间。
    /// </summary>
    [AvaloniaFact]
    public async Task CollapsedSidebarMigratesToHiddenDockAndReopensFromMenu()
    {
        using var context = new WorkspaceSidebarTestContext(316, true);
        var window = new MainWindow { DataContext = context.ViewModel };

        try
        {
            window.Show();
            await window.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(window.Workbench);
            var room = context.ViewModel.SelectedWorkspace!.SelectedRoom;
            Assert.False(workbench.Layouts.IsVisible(WorkbenchPanelIds.WORKSPACE));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.Name == "SidebarToggleButton");
            var entry = workbench.MenuCatalog.Entries.Single(group => group.Id == "views").Children!
                .Single(item => item.Id == "panel:workspace");
            entry.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(workbench.Layouts.IsVisible(WorkbenchPanelIds.WORKSPACE));
            Assert.Same(room, workbench.Context.CurrentRoom);
            entry.Command.Execute(null);
            Assert.True(workbench.Layouts.IsVisible(WorkbenchPanelIds.WORKSPACE));
        }
        finally
        {
            await window.CloseWorkbenchAsync();
        }

        var restored = await new WorkspaceLayoutStore(context.StateService.ConfigDirectory).LoadAsync(context.ViewModel.Settings);
        Assert.DoesNotContain(WorkbenchPanelIds.WORKSPACE, restored.CurrentLayout.HiddenPanelIds);
        Assert.Equal(316, context.StateService.Load().Settings.SidebarWidth);
    }

    /// <summary>
    /// 全部关闭后退出仍保存八个隐藏面板，不被浮窗清理覆盖为空配置。
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingAllPanelsPersistsCompleteHiddenLayout()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var window = new MainWindow { DataContext = context.ViewModel };
        window.Show();
        await window.Initialization;

        try
        {
            var workbench = Assert.IsType<WorkbenchSession>(window.Workbench);

            foreach (var id in WorkbenchPanelIds.All)
            {
                workbench.Layouts.HidePanel(id);
            }

            Assert.Equal(8, workbench.Layouts.Capture().HiddenPanelIds.Count);
        }
        finally
        {
            await window.CloseWorkbenchAsync();
        }

        Assert.False(window.IsVisible);
        var saved = await new WorkspaceLayoutStore(context.StateService.ConfigDirectory).LoadAsync();
        Assert.Null(saved.CurrentLayout.Main);
        Assert.Equal(8, saved.CurrentLayout.HiddenPanelIds.Count);
        Assert.True(WorkspaceLayoutValidator.TryValidate(saved, out var error), error);
    }

    /// <summary>
    /// 直播间切换只变更共享上下文，不重建面板或修改用户布局。
    /// </summary>
    [AvaloniaFact]
    public async Task RoomSelectionPreservesGlobalLayoutAndPanelInstances()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var window = new MainWindow { DataContext = context.ViewModel };

        try
        {
            window.Show();
            await window.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(window.Workbench);
            workbench.Layouts.ApplyPreset(WorkspaceLayoutPresets.LYRICS);
            workbench.Layouts.HidePanel(WorkbenchPanelIds.HISTORY);
            var before = WorkspaceLayoutSerializer.Serialize(workbench.Layouts.CaptureFile());
            var panels = workbench.Panels.Panels.ToDictionary(pair => pair.Key, pair => pair.Value);
            var next = context.ViewModel.SelectedWorkspace!.AddRoom("200", "第二个直播间");
            context.ViewModel.SelectedWorkspace.SelectedRoom = next;

            Assert.Same(next, workbench.Context.CurrentRoom);
            Assert.Equal(before, WorkspaceLayoutSerializer.Serialize(workbench.Layouts.CaptureFile()));

            foreach (var (id, panel) in panels)
            {
                Assert.Same(panel, workbench.Panels.Panels[id]);
            }
        }
        finally
        {
            await window.CloseWorkbenchAsync();
        }
    }
}
