using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证全局工作台的菜单、设置、浮窗和有序退出。
/// </summary>
public class WorkbenchWindowTests
{
    /// <summary>
    /// 设置保持单实例并阻止业务窗交互，保留停靠或浮动播放器的显示，关闭后保存并恢复交互。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsAreSingleInstanceModalAndSaveOnNativeClose(bool floatingPlayer)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.FloatPanel(floatingPlayer ? WorkbenchPanelIds.LIVE_PLAYER : WorkbenchPanelIds.COMPOSER);
            Dispatcher.UIThread.RunJobs();
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            var video = workbench.Panels.Player.FindControl<Border>("LivePlayerHost")!;
            Assert.True(video.IsEffectivelyVisible);
            workbench.MenuCatalog.SettingsCommand.Execute(null);
            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("settings"));
            workbench.MenuCatalog.SettingsCommand.Execute(null);

            Assert.Same(settings, workbench.Windows.FindAuxiliary("settings"));
            Assert.True(workbench.Windows.IsModal);
            Assert.False(main.IsEnabled);
            Assert.False(floating.IsEnabled);
            Assert.True(settings.IsEnabled);
            Assert.True(video.IsEffectivelyVisible);
            Assert.False(video.IsEffectivelyEnabled);
            context.ViewModel.UseWindowMenuOnMac = true;
            var recording = context.ViewModel.ShortcutBindingGroups.SelectMany(group => group.Bindings).First();
            context.ViewModel.BeginRecordShortcut(recording);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            settings.Closed += (_, _) => closed.TrySetResult();
            settings.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(context.ViewModel.IsSettingsOpen);
            Assert.False(context.ViewModel.IsRecordingShortcut);
            Assert.True(context.StateService.Load().Settings.UseWindowMenuOnMac);
            Assert.False(workbench.Windows.IsModal);
            Assert.True(main.IsEnabled);
            Assert.True(floating.IsEnabled);
            Assert.True(video.IsEffectivelyVisible);
            Assert.True(video.IsEffectivelyEnabled);
            Assert.True(main.FindControl<Menu>("WindowMenu")!.IsVisible);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 浮窗使用同一路由，单次 Tab 只轮切一次，主窗退出等待浮窗和布局清理。
    /// </summary>
    [AvaloniaFact]
    public async Task FloatingShortcutRunsOnceAndShutdownRetainsFloatingLayout()
    {
        using var context = new MarkGroupCycleTestContext();
        var main = new MainWindow { DataContext = context.ViewModel };
        Window? floating = null;

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
            Dispatcher.UIThread.RunJobs();
            floating = Assert.Single(workbench.Layouts.FloatingWindows);
            floating.Activate();
            workbench.Panels.FocusInputDraft();
            Dispatcher.UIThread.RunJobs();
            floating.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            floating.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            await context.ViewModel.CycleWorkspaceMarkGroupCommand.ExecutionTask!;

            Assert.Equal("b", context.ViewModel.SelectedWorkspaceMarkGroup!.Id);

            if (OperatingSystem.IsMacOS())
            {
                var mainMenu = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(main));
                var floatingMenu = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(floating));
                Assert.NotSame(mainMenu, floatingMenu);
                var mainSettings = ((NativeMenuItem)mainMenu.Items[0]).Menu!.Items[0] as NativeMenuItem;
                var floatingSettings = ((NativeMenuItem)floatingMenu.Items[0]).Menu!.Items[0] as NativeMenuItem;
                Assert.Same(mainSettings!.Command, floatingSettings!.Command);
            }
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }

        Assert.False(main.IsVisible);
        Assert.False(floating!.IsVisible);
        var saved = await new WorkspaceLayoutStore(context.StateService.ConfigDirectory).LoadAsync();
        Assert.Single(saved.CurrentLayout.FloatingWindows);
        Assert.True(WorkspaceLayoutValidator.TryValidate(saved, out var error), error);
    }

    /// <summary>
    /// 当前平台的设置组合键在主窗和输入浮窗中都通过统一路由打开设置。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlatformSettingsGestureOpensSettingsFromBusinessHost(bool floatingHost)
    {
        using var context = new MarkGroupCycleTestContext();
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            Window host = main;

            if (floatingHost)
            {
                workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
                Dispatcher.UIThread.RunJobs();
                host = Assert.Single(workbench.Layouts.FloatingWindows);
            }

            host.Activate();
            workbench.Panels.FocusInputDraft();
            Dispatcher.UIThread.RunJobs();
            var modifiers = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            host.KeyPressQwerty(PhysicalKey.Comma, modifiers);
            host.KeyReleaseQwerty(PhysicalKey.Comma, modifiers);

            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("settings"));
            Assert.True(context.ViewModel.IsSettingsOpen);
            Assert.True(settings.IsVisible);
            Assert.Same(main, settings.Owner);
            Assert.True(workbench.Windows.IsModal);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 非模态更新窗口打开后，主窗和输入浮窗的业务快捷键仍只执行一次。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonModalUpdateKeepsBusinessShortcutsAvailable(bool floatingHost)
    {
        using var context = new MarkGroupCycleTestContext();
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            Window host = main;

            if (floatingHost)
            {
                workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
                Dispatcher.UIThread.RunJobs();
                host = Assert.Single(workbench.Layouts.FloatingWindows);
            }

            context.ViewModel.IsUpdateDialogOpen = true;
            var update = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("update"));
            Assert.False(workbench.Windows.IsModal);
            Assert.True(main.IsEnabled);
            Assert.True(host.IsEnabled);
            host.Activate();
            workbench.Panels.FocusInputDraft();
            Dispatcher.UIThread.RunJobs();
            host.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            host.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Assert.NotNull(context.ViewModel.CycleWorkspaceMarkGroupCommand.ExecutionTask);
            await context.ViewModel.CycleWorkspaceMarkGroupCommand.ExecutionTask!;

            Assert.Equal("b", context.ViewModel.SelectedWorkspaceMarkGroup!.Id);
            Assert.True(context.ViewModel.IsUpdateDialogOpen);
            Assert.True(update.IsVisible);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 空工作区和切换布局期间菜单仍能恢复面板，辅助窗口不依赖房间存在。
    /// </summary>
    [AvaloniaFact]
    public async Task EmptyWorkspaceCanUseLayoutsAndAbout()
    {
        using var context = new MarkGroupCycleTestContext(false);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            Assert.Null(workbench.Context.CurrentRoom);
            var help = workbench.MenuCatalog.Entries.Single(group => group.Id == "help");
            help.Children!.Single(item => item.Id == "about").Command!.Execute(null);
            var about = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("about"));
            Assert.Same(main, about.Owner);
            workbench.Layouts.ApplyPreset(WorkspaceLayoutPresets.FORWARDING);
            Assert.True(WorkspaceLayoutValidator.TryValidate(workbench.Layouts.Capture(), out var error), error);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }
}
