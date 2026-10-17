using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证工作区设置的独立入口、模态生命周期与关闭保存。
/// </summary>
public class WorkspaceSettingsWindowTests
{
    /// <summary>
    /// 全局设置不再提供工作区分区或实例化工作区设置表单。
    /// </summary>
    [AvaloniaFact]
    public async Task GlobalSettingsExcludeWorkspaceSettings()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.MenuCatalog.SettingsCommand.Execute(null);
            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("settings"));

            Assert.DoesNotContain(context.ViewModel.SettingsSectionOptions, option => option.Key == "workspace");
            Assert.Empty(settings.GetVisualDescendants().OfType<WorkspaceSettingsView>());
            Assert.Null(workbench.Windows.FindAuxiliary("workspace-settings"));
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 仅工作区设置菜单打开右键目标的独立单实例窗口，并暂停主窗和业务浮窗。
    /// </summary>
    [AvaloniaFact]
    public async Task ContextMenuOpensClickedWorkspaceInSingleModalWindow()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var target = context.ViewModel.SelectedWorkspace!;
        await context.ViewModel.AddWorkspaceAsync();
        Assert.NotSame(target, context.ViewModel.SelectedWorkspace);
        var section = context.ViewModel.SettingsSectionOptions.Single(option => option.Key == "misc");
        context.ViewModel.SelectSettingsSection(section);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            var sidebar = Assert.IsType<WorkspaceSidebarView>(
                workbench.Layouts.PanelAdapters[WorkbenchPanelIds.WORKSPACE].View);
            var row = sidebar.GetVisualDescendants().OfType<Grid>().Single(grid =>
                grid.Classes.Contains("workspaceTreeRow") && ReferenceEquals(grid.DataContext, target));
            var menu = Assert.IsType<ContextMenu>(row.ContextMenu);
            menu.Open(row);
            Dispatcher.UIThread.RunJobs();
            var entry = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "工作区设置"));
            Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "重命名"));
            var roomButton = sidebar.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Classes.Contains("treeRoomButton") && ReferenceEquals(button.DataContext, target.SelectedRoom));
            Assert.DoesNotContain(roomButton.ContextMenu!.Items.OfType<MenuItem>(), item => Equals(item.Header, "修改"));
            Assert.Same(target, entry.DataContext);
            entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Close();
            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("workspace-settings"));
            Dispatcher.UIThread.RunJobs();
            settings.UpdateLayout();

            Assert.Equal("工作区设置", settings.Title);
            Assert.Same(target, context.ViewModel.SelectedWorkspace);
            Assert.Same(section, context.ViewModel.SelectedSettingsSection);
            Assert.False(context.ViewModel.IsSettingsOpen);
            Assert.Null(workbench.Windows.FindAuxiliary("settings"));
            var form = Assert.Single(settings.GetVisualDescendants().OfType<WorkspaceSettingsView>());
            Assert.Equal(target.Name, form.FindControl<TextBox>("WorkspaceNameInput")!.Text);
            Assert.True(workbench.Windows.IsModal);
            Assert.False(main.IsEnabled);
            Assert.False(floating.IsEnabled);
            Assert.True(settings.IsEnabled);
            context.ViewModel.OpenWorkspaceSettings(target);
            Assert.Same(settings, workbench.Windows.FindAuxiliary("workspace-settings"));
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 页面保存按钮与系统关闭按钮均保存工作区名称和直播间资料，并恢复业务窗交互。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingWorkspaceSettingsSavesChanges(bool closeFromPage)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            var workspace = context.ViewModel.SelectedWorkspace!;
            context.ViewModel.OpenWorkspaceSettings(workspace);
            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("workspace-settings"));
            workspace.Name = "保存后的工作区";
            workspace.Rooms[0].RoomName = "保存后的房间";
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            settings.Closed += (_, _) => closed.TrySetResult();
            RequestClose(settings, closeFromPage);
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var saved = Assert.Single(context.StateService.Load().Workspaces);

            Assert.Equal(workspace.Name, saved.Name);
            Assert.Equal(workspace.Rooms[0].RoomName, Assert.Single(saved.LiveRooms).RoomName);
            Assert.Null(workbench.Windows.FindAuxiliary("workspace-settings"));
            Assert.False(workbench.Windows.IsModal);
            Assert.True(main.IsEnabled);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 保存失败时独立窗口保持打开并允许重试，修复存储后可正常保存关闭。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveFailureKeepsWorkspaceSettingsOpenForRetry(bool closeFromPage)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };
        var settingsPath = context.StateService.SettingsFilePath;

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            context.ViewModel.OpenWorkspaceSettings(context.ViewModel.SelectedWorkspace);
            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("workspace-settings"));
            File.Delete(settingsPath);
            Directory.CreateDirectory(settingsPath);
            RequestClose(settings, closeFromPage);
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (!settings.Title!.StartsWith("保存失败：", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.StartsWith("保存失败：", settings.Title);
            Assert.True(settings.IsVisible);
            Assert.True(((Control)settings.Content!).IsEnabled);
            Assert.True(workbench.Windows.IsModal);
            Assert.False(main.IsEnabled);
            Directory.Delete(settingsPath);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            settings.Closed += (_, _) => closed.TrySetResult();
            RequestClose(settings, closeFromPage);
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(File.Exists(settingsPath));
            Assert.Null(workbench.Windows.FindAuxiliary("workspace-settings"));
            Assert.False(workbench.Windows.IsModal);
        }
        finally
        {
            if (Directory.Exists(settingsPath))
            {
                Directory.Delete(settingsPath);
            }

            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 删除确认属于工作区设置窗口，取消后恢复设置，删除当前工作区后关闭其设置。
    /// </summary>
    [AvaloniaFact]
    public async Task DeletingWorkspaceClosesItsSettingsAfterNestedConfirmation()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        await context.ViewModel.AddWorkspaceAsync();
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            var workspace = context.ViewModel.SelectedWorkspace!;
            context.ViewModel.OpenWorkspaceSettings(workspace);
            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("workspace-settings"));
            var view = settings.GetVisualDescendants().OfType<WorkspaceSettingsView>().Single();
            view.OpenDeleteWorkspaceDialogCommand.Execute(workspace);
            var confirm = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("confirm"));

            Assert.Same(settings, confirm.Owner);
            Assert.False(settings.IsEnabled);
            context.ViewModel.CloseDeleteDialog();
            Assert.True(settings.IsEnabled);
            Assert.False(main.IsEnabled);
            view.OpenDeleteWorkspaceDialogCommand.Execute(workspace);
            await context.ViewModel.ConfirmDeleteDialogAsync();

            Assert.DoesNotContain(workspace, context.ViewModel.Workspaces);
            Assert.False(settings.IsVisible);
            Assert.Null(workbench.Windows.FindAuxiliary("workspace-settings"));
            Assert.Null(workbench.Windows.FindAuxiliary("confirm"));
            Assert.False(workbench.Windows.IsModal);
            Assert.True(main.IsEnabled);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 主窗退出等待工作区设置保存并关闭所有辅助窗口。
    /// </summary>
    [AvaloniaFact]
    public async Task ShutdownSavesAndClosesWorkspaceSettings()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            context.ViewModel.OpenWorkspaceSettings(context.ViewModel.SelectedWorkspace);
            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("workspace-settings"));
            context.ViewModel.SelectedWorkspace!.Name = "退出时保存的工作区";
            await main.CloseWorkbenchAsync();

            Assert.False(main.IsVisible);
            Assert.False(settings.IsVisible);
            Assert.Null(workbench.Windows.FindAuxiliary("workspace-settings"));
            Assert.Equal("退出时保存的工作区", Assert.Single(context.StateService.Load().Workspaces).Name);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    private static void RequestClose(Window settings, bool closeFromPage)
    {
        if (closeFromPage)
        {
            var save = settings.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "SaveWorkspaceSettingsButton");
            save.Command!.Execute(null);
        }
        else
        {
            settings.Close();
        }
    }
}
