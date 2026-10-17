using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lucide.Avalonia;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Menus;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 在真实菜单附加属性边界验证 Avalonia 原生 exporter 的身份与窗口生命周期契约。
/// </summary>
public class WorkbenchNativeMenuLifecycleTests
{
    /// <summary>
    /// headless 探针必须实际拒绝原生 exporter 不允许的清空和根对象替换。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExporterBoundaryRejectsReplacingAnExportedRoot(bool clearMenu)
    {
        var window = new Window();
        using var exporter = new NativeMenuExporterProbe(window);

        try
        {
            NativeMenu.SetMenu(window, new());
            var error = Assert.Throws<ArgumentException>(() => NativeMenu.SetMenu(window, clearMenu ? null : new()));

            Assert.Equal("menu", error.ParamName);
            Assert.StartsWith("The menu being updated does not match.", error.Message);
            Assert.Equal(1, exporter.AssignmentCount);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 探针实际编码新菜单项的位图，能发现结构导出前已被提前释放的共享图标。
    /// </summary>
    [AvaloniaFact]
    public void ExporterRejectsIconsDisposedBeforeQueuedExport()
    {
        var window = new Window();
        using var exporter = new NativeMenuExporterProbe(window);
        using var icons = new WorkbenchMenuIcons();

        try
        {
            var root = new NativeMenu();
            NativeMenu.SetMenu(window, root);
            root.Items.Add(new NativeMenuItem("设置") { Icon = icons.GetNative(LucideIconKind.Settings) });
            Assert.True(exporter.HasPendingChanges);
            Assert.Equal(0, exporter.IconExportCount);
            icons.Dispose();

            Assert.NotNull(Record.Exception(exporter.ExportPendingChanges));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 另存布局后立即关闭主窗，已排队的新菜单图标先完成导出，再释放整个会话的缓存。
    /// </summary>
    [AvaloniaFact]
    public async Task PresetCreatedImmediatelyBeforeCloseExportsIconsBeforeCacheDisposal()
    {
        using var context = new MarkGroupCycleTestContext();
        var main = new Window { Width = 1200, Height = 800 };
        using var exporter = new NativeMenuExporterProbe(main);
        var workbench = await OpenWorkbenchAsync(main, new(), context.ViewModel);

        try
        {
            var root = Assert.IsType<NativeMenu>(exporter.Root);
            var exportsBeforeSave = exporter.IconExportCount;
            Assert.True(exportsBeforeSave > 0);
            workbench.Layouts.SaveAs("关闭前新建布局");
            var layouts = root.Items.OfType<NativeMenuItem>().Single(item => item.Header == "布局").Menu!;
            var preset = layouts.Items.OfType<NativeMenuItem>().Single(item => item.Header == "关闭前新建布局");
            var icon = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(preset.Icon);
            Assert.True(exporter.HasPendingChanges);
            Assert.Equal(exportsBeforeSave, exporter.IconExportCount);

            main.Close();

            Assert.True(exporter.IsClosed);
            Assert.False(main.IsVisible);
            Assert.True(exporter.HasPendingChanges);
            Assert.Equal(0, exporter.IconExportsAfterClose);
            using var stream = new MemoryStream();
            icon.Save(stream);
            Assert.True(stream.Length > 0);

            Dispatcher.UIThread.RunJobs();

            Assert.False(exporter.HasPendingChanges);
            Assert.True(exporter.IconExportsAfterClose > 0);
            Assert.True(exporter.IconExportCount > exportsBeforeSave);
            Assert.Same(root, NativeMenu.GetMenu(main));
            Assert.Equal(1, exporter.AssignmentCount);
            Assert.NotNull(Record.Exception(() => icon.Save(stream)));
        }
        finally
        {
            await workbench.DisposeAsync();
            main.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 初始菜单模式、往返切换和预设结构重建均保留已导出的根对象与当前显示模式。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MenuModesAndPresetChangesKeepTheExportedRoot(bool startsWithWindowMenu)
    {
        using var context = new MarkGroupCycleTestContext();
        context.ViewModel.Settings.UseWindowMenuOnMac = startsWithWindowMenu;
        var window = new Window { Width = 1200, Height = 800 };
        var windowMenu = new Menu();
        using var exporter = new NativeMenuExporterProbe(window);
        var workbench = await OpenWorkbenchAsync(window, windowMenu, context.ViewModel);

        try
        {
            var root = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(window));
            Assert.Same(root, exporter.Root);
            AssertMenuMode(root, windowMenu, startsWithWindowMenu);

            foreach (var windowMode in new[] { !startsWithWindowMenu, startsWithWindowMenu })
            {
                context.ViewModel.UseWindowMenuOnMac = windowMode;
                var id = workbench.Layouts.SaveAs(windowMode ? "窗口菜单布局" : "原生菜单布局");
                exporter.ExportPendingChanges();
                var layoutsMenu = root.Items.OfType<NativeMenuItem>().Single(item => item.Header == "布局").Menu!;
                Assert.Contains(layoutsMenu.Items.OfType<NativeMenuItem>(), item => item.Header ==
                    (windowMode ? "窗口菜单布局" : "原生菜单布局"));
                Assert.True(workbench.Layouts.Rename(id, "已重命名布局"));
                exporter.ExportPendingChanges();
                Assert.Contains(layoutsMenu.Items.OfType<NativeMenuItem>(), item => item.Header == "已重命名布局");
                Assert.True(workbench.Layouts.Delete(id));
                exporter.ExportPendingChanges();

                Assert.Same(root, NativeMenu.GetMenu(window));
                AssertMenuMode(root, windowMenu, windowMode);
                Assert.Equal(1, exporter.AssignmentCount);
            }

            workbench.SetShutdownPending(true);
            await workbench.SaveForShutdownAsync();
            await workbench.DisposeAsync();
            Assert.Same(root, NativeMenu.GetMenu(window));
            Assert.Equal(1, exporter.AssignmentCount);
            var icon = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(
                root.Items.OfType<NativeMenuItem>().First().Menu!.Items.OfType<NativeMenuItem>().Single().Icon);
            using var stream = new MemoryStream();
            icon.Save(stream);
            Assert.True(stream.Length > 0);
        }
        finally
        {
            await workbench.DisposeAsync();
            window.Close();
        }

        Assert.True(exporter.IsClosed);
    }

    /// <summary>
    /// 浮窗独占菜单树，关闭后旧树不再参与预设刷新或窗口菜单模式切换。
    /// </summary>
    [AvaloniaFact]
    public async Task ClosedFloatingWindowKeepsItsExporterRootFrozen()
    {
        using var context = new MarkGroupCycleTestContext();
        var main = new Window { Width = 1200, Height = 800 };
        using var mainExporter = new NativeMenuExporterProbe(main);
        var workbench = await OpenWorkbenchAsync(main, new(), context.ViewModel);

        try
        {
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
            Dispatcher.UIThread.RunJobs();
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            using var floatingExporter = new NativeMenuExporterProbe(floating);
            var mainRoot = Assert.IsType<NativeMenu>(mainExporter.Root);
            var floatingRoot = Assert.IsType<NativeMenu>(floatingExporter.Root);
            var mainObjects = EnumerateMenuObjects(mainRoot).ToHashSet();
            Assert.DoesNotContain(EnumerateMenuObjects(floatingRoot), mainObjects.Contains);
            var mainSettings = ((NativeMenuItem)mainRoot.Items[0]).Menu!.Items.OfType<NativeMenuItem>().Single();
            var floatingSettings = ((NativeMenuItem)floatingRoot.Items[0]).Menu!.Items.OfType<NativeMenuItem>().Single();
            Assert.Same(mainSettings.Command, floatingSettings.Command);
            Assert.Same(mainSettings.Icon, floatingSettings.Icon);

            floating.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(floatingExporter.IsClosed);
            Assert.False(floating.IsVisible);
            Assert.Empty(workbench.Layouts.FloatingWindows);
            var frozenObjects = EnumerateMenuObjects(floatingRoot).ToArray();
            workbench.Layouts.SaveAs("浮窗关闭后的布局");
            context.ViewModel.UseWindowMenuOnMac = true;
            mainExporter.ExportPendingChanges();

            Assert.Same(floatingRoot, NativeMenu.GetMenu(floating));
            Assert.Equal(frozenObjects, EnumerateMenuObjects(floatingRoot));
            Assert.Equal(1, floatingExporter.AssignmentCount);
            Assert.Equal(1, mainExporter.AssignmentCount);
            using var stream = new MemoryStream();
            mainSettings.Icon!.Save(stream);
            Assert.True(stream.Length > 0);
        }
        finally
        {
            await workbench.SaveForShutdownAsync();
            await workbench.DisposeAsync();
            main.Close();
        }
    }

    /// <summary>
    /// 产品主窗口的退出入口不会替换已经导出的菜单，也不会把菜单异常误报为保存失败。
    /// </summary>
    [AvaloniaFact]
    public async Task MainWindowClosesWithItsExistingNativeMenu()
    {
        using var context = new MarkGroupCycleTestContext();
        var main = new MainWindow { DataContext = context.ViewModel };
        using var exporter = new NativeMenuExporterProbe(main);

        try
        {
            main.Show();
            await main.Initialization;
            Assert.NotNull(main.Workbench);
            main.Workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
            Dispatcher.UIThread.RunJobs();
            var floating = Assert.Single(main.Workbench.Layouts.FloatingWindows);
            using var floatingExporter = new NativeMenuExporterProbe(floating);
            var root = NativeMenu.GetMenu(main);
            await main.CloseWorkbenchAsync();

            Assert.False(main.IsVisible);
            Assert.False(floating.IsVisible);
            Assert.True(exporter.IsClosed);
            Assert.True(floatingExporter.IsClosed);
            Assert.Same(root, NativeMenu.GetMenu(main));
            Assert.DoesNotContain("无法完成退出保存：", context.ViewModel.StatusMessage);

            if (OperatingSystem.IsMacOS())
            {
                Assert.NotNull(root);
                Assert.Equal(1, exporter.AssignmentCount);
                Assert.Equal(1, floatingExporter.AssignmentCount);
            }
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 工作台仅添加和移除自己的应用设置项，不替换应用根菜单或影响系统菜单项。
    /// </summary>
    [AvaloniaFact]
    public async Task ApplicationSettingsItemKeepsExistingMenuOwnership()
    {
        await using var context = new WorkbenchLayoutTestContext();
        using var catalog = new WorkbenchMenuCatalog(context.Controller, () => { }, () => { }, () => { },
            () => { }, () => { }, () => false);
        var application = new Application();
        using var exporter = new NativeMenuExporterProbe(application);
        var root = new NativeMenu();
        var systemItem = new NativeMenuItem("系统菜单项");
        root.Items.Add(systemItem);
        NativeMenu.SetMenu(application, root);

        using var icons = new WorkbenchMenuIcons();

        using (var applicationMenu = new WorkbenchApplicationMenu(application, catalog, icons))
        {
            exporter.ExportPendingChanges();
            Assert.Same(root, NativeMenu.GetMenu(application));
            Assert.Equal(2, root.Items.Count);
            var settings = Assert.IsType<NativeMenuItem>(root.Items[0]);
            Assert.Same(catalog.SettingsCommand, settings.Command);
            Assert.NotNull(settings.Icon);
            Assert.Same(root, settings.Parent);
            Assert.Same(root, systemItem.Parent);
        }

        exporter.ExportPendingChanges();
        Assert.Same(systemItem, Assert.Single(root.Items));
        Assert.Same(root, NativeMenu.GetMenu(application));
        Assert.Equal(1, exporter.AssignmentCount);
    }

    private static async Task<WorkbenchSession> OpenWorkbenchAsync(Window window, Menu menu,
        MainWindowViewModel viewModel)
    {
        var workbench = await WorkbenchSession.CreateAsync(window, menu, viewModel, supportsNativeMenus: true);
        DockPanel.SetDock(menu, Avalonia.Controls.Dock.Top);
        window.Content = new DockPanel { Children = { menu, workbench.Layouts.Host } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return workbench;
    }

    private static void AssertMenuMode(NativeMenu nativeMenu, Menu windowMenu, bool usesWindowMenu)
    {
        Assert.Equal(usesWindowMenu, windowMenu.IsVisible);
        Assert.NotEmpty(nativeMenu.Items);
        Assert.All(nativeMenu.Items.OfType<NativeMenuItem>(), item => Assert.Equal(!usesWindowMenu, item.IsVisible));
    }

    private static IEnumerable<AvaloniaObject> EnumerateMenuObjects(NativeMenu menu)
    {
        yield return menu;

        foreach (var item in menu.Items)
        {
            yield return item;

            if (item is NativeMenuItem { Menu: { } child })
            {
                foreach (var descendant in EnumerateMenuObjects(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
