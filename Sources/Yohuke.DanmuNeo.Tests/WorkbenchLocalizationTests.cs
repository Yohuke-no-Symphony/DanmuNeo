using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证工作台现有菜单、Dock、窗口和布局草稿的实时语言刷新。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class WorkbenchLocalizationTests
{
    /// <summary>
    /// 切换语言保留原生菜单根、顶层菜单项、浮窗、面板、布局文件和另存名称草稿。
    /// </summary>
    [AvaloniaFact]
    public async Task ExistingMenusFloatingPanelsAndSaveWindowRefreshWithoutChangingLayout()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        using var context = new MarkGroupCycleTestContext(hasRoom: false);
        var main = new Window { Width = 1200, Height = 800 };
        var menu = new Menu();
        using var exporter = new NativeMenuExporterProbe(main);
        var workbench = await WorkbenchSession.CreateAsync(main, menu, context.ViewModel, supportsNativeMenus: true);
        DockPanel.SetDock(menu, Avalonia.Controls.Dock.Top);
        main.Content = new DockPanel { Children = { menu, workbench.Layouts.Host } };
        main.Show();

        try
        {
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
            Dispatcher.UIThread.RunJobs();
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            var customId = workbench.Layouts.SaveAs("Default");
            await workbench.Layouts.FlushAsync();
            Dispatcher.UIThread.RunJobs();
            var root = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(main));
            var floatingRoot = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(floating));
            var applicationRoot = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(Application.Current!));
            var applicationSettings = Assert.IsType<NativeMenuItem>(applicationRoot.Items[0]);
            var nativeGroups = root.Items.OfType<NativeMenuItem>().ToArray();
            var windowGroups = menu.Items.OfType<MenuItem>().ToArray();
            var panels = workbench.Layouts.PanelAdapters.ToDictionary();
            var rootDock = workbench.Layouts.RootDock;
            var layoutBefore = WorkspaceLayoutSerializer.Serialize(workbench.Layouts.CaptureFile());
            var layoutPath = Path.Combine(context.ViewModel.ConfigDirectory, "layouts.json");
            var layoutBytes = File.ReadAllBytes(layoutPath);
            var settingsBytes = File.ReadAllBytes(context.StateService.SettingsFilePath);
            var layoutChanges = 0;
            workbench.Layouts.Changed += (_, _) => layoutChanges++;
            var saveAs = workbench.MenuCatalog.Entries.Single(group => group.Id == "layouts")
                .Children!.Single(entry => entry.Id == "layout-save-as");
            saveAs.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var saveWindow = Assert.IsType<WorkbenchAuxiliaryWindow>(workbench.Windows.FindAuxiliary("save-layout"));
            var name = saveWindow.GetVisualDescendants().OfType<TextBox>().Single();
            name.Text = "  默认  ";
            var save = saveWindow.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, Localization.Get("Window.Save")));
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var validationMessage = saveWindow.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == Localization.Get("Layout.Validation.InvalidPresetName"));

            Localization.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();

            Assert.Same(root, NativeMenu.GetMenu(main));
            Assert.Same(floatingRoot, NativeMenu.GetMenu(floating));
            Assert.Same(applicationRoot, NativeMenu.GetMenu(Application.Current!));
            Assert.Same(applicationSettings, applicationRoot.Items[0]);
            Assert.Equal("Settings…", applicationSettings.Header);
            Assert.Equal(1, exporter.AssignmentCount);
            Assert.Equal(nativeGroups, root.Items.OfType<NativeMenuItem>());
            Assert.Equal(windowGroups, menu.Items.OfType<MenuItem>());
            Assert.Equal(new[] { "Application", "View", "Layout", "Help" }, nativeGroups.Select(group => group.Header));
            Assert.Equal(new[] { "Application", "View", "Layout", "Help" }, windowGroups.Select(group => group.Header));
            Assert.Same(floating, Assert.Single(workbench.Layouts.FloatingWindows));
            Assert.Equal("Composer — DanmuNeo", floating.Title);
            Assert.Equal("Save Layout As", saveWindow.Title);
            Assert.Equal("Layout name", name.Watermark);
            Assert.Equal("Save", save.Content);
            Assert.Equal("  默认  ", name.Text);
            Assert.Equal(Localization.Get("Layout.Validation.InvalidPresetName"), validationMessage.Text);
            Assert.Equal("Composer", panels[WorkbenchPanelIds.COMPOSER].Title);
            Assert.All(panels, pair => Assert.Same(pair.Value, workbench.Layouts.PanelAdapters[pair.Key]));
            Assert.Same(rootDock, workbench.Layouts.RootDock);
            Assert.Equal(customId, workbench.Layouts.CurrentPresetId);
            Assert.Equal("Default", Assert.Single(workbench.Layouts.CaptureFile().UserPresets).Name);
            Assert.Equal(layoutBefore, WorkspaceLayoutSerializer.Serialize(workbench.Layouts.CaptureFile()));
            Assert.Equal(layoutBytes, File.ReadAllBytes(layoutPath));
            Assert.Equal(settingsBytes, File.ReadAllBytes(context.StateService.SettingsFilePath));
            Assert.Equal(0, layoutChanges);

            saveWindow.Close();
            var closedContent = save.Content;
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(closedContent, save.Content);
            Assert.Equal("Save Layout As", saveWindow.Title);
        }
        finally
        {
            await workbench.DisposeAsync();
            main.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 管理行按稳定 ID 更新内置显示名称，自定义名称、选择、草稿和校验结果不随语言变化。
    /// </summary>
    [AvaloniaFact]
    public async Task LayoutManagerRefreshesRowsInPlaceAndPreservesCustomNamesAndDrafts()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        await using var context = new WorkbenchLayoutTestContext();
        var id = context.Controller.SaveAs("Default");
        using var manager = new LayoutPresetManagerViewModel(context.Controller, (_, _) => Task.FromResult(false));
        var rows = manager.Presets.ToArray();
        var selected = manager.Selected;
        manager.Name = "默认";
        manager.RenameCommand.Execute(null);
        Assert.True(manager.HasError);
        var before = WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile());
        var changes = 0;
        context.Controller.Changed += (_, _) => changes++;

        Localization.SetLanguage("en-US");

        Assert.Equal(rows, manager.Presets);
        Assert.Same(selected, manager.Selected);
        Assert.Equal(id, manager.Selected!.Id);
        Assert.Equal("默认", manager.Name);
        Assert.Equal(Localization.Get("Layout.Validation.InvalidPresetName"), manager.Error);
        Assert.Equal("Default", manager.Presets.Single(row => row.Id == id).Name);
        Assert.Equal("Default (built in)", manager.Presets.Single(row => row.Id == WorkspaceLayoutPresets.DEFAULT).DisplayName);
        Assert.True(WorkspaceLayoutValidator.TryValidate(context.Controller.CaptureFile(), out var error), error);
        Assert.Equal(before, WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile()));
        Assert.Equal(0, changes);

        var notifications = 0;
        rows[0].PropertyChanged += (_, _) => notifications++;
        manager.Dispose();
        Localization.SetLanguage("zh-CN");
        Assert.Equal(0, notifications);
    }

    /// <summary>
    /// 日志级别标签变化保持筛选索引、查询、选中记录和发生时的日志快照。
    /// </summary>
    [AvaloniaFact]
    public void LogLevelRefreshPreservesTheActiveFilterSelectionAndHistoricalText()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        using var log = new AppLogService();
        log.Error("原始来源", "原始日志正文");
        using var view = new LogPanelView(log);
        var model = Assert.IsType<LogPanelViewModel>(view.DataContext);
        var window = new Window { Width = 600, Height = 400, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            model.FilterIndex = 3;
            model.FilterText = "原始日志";
            model.SelectedEntry = Assert.Single(model.Entries);
            var selected = model.SelectedEntry;
            var text = selected.FullText;
            var levels = model.Levels;
            var filter = view.FindControl<ComboBox>("LogLevelFilter")!;

            Localization.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();

            Assert.Same(levels, model.Levels);
            Assert.Equal("All levels", model.Levels[0]);
            Assert.Equal(3, model.FilterIndex);
            Assert.Equal(3, filter.SelectedIndex);
            Assert.Equal("原始日志", model.FilterText);
            Assert.Same(selected, model.SelectedEntry);
            Assert.Equal(text, selected.FullText);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 布局异常保留精确类型、参数和发生时消息，关闭失败窗口按当前语言重新展示原因。
    /// </summary>
    [AvaloniaFact]
    public void FailedCloseRefreshesCapturedLayoutErrorsWithoutChangingTheOriginalException()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var failure = Assert.Throws<InvalidDataException>(() => WorkspaceLayoutSerializer.Deserialize("[]"));
        var originalMessage = failure.Message;
        var nameFailure = LayoutValidationFailure.CreateException("Layout.CustomPresetMissing", "name");
        Assert.IsType<ArgumentException>(nameFailure);
        Assert.Equal("name", nameFailure.ParamName);
        var owner = new Window { Width = 800, Height = 600 };
        using var windows = new WorkbenchWindowRegistry(owner);
        var canClose = false;
        owner.Show();
        var window = windows.OpenLocalizedAuxiliary("failed-close", "Window.SaveLayout.Title", _ => new TextBlock(),
            beforeClose: () => canClose ? Task.CompletedTask : Task.FromException(failure),
            width: 480, sizing: WorkbenchAuxiliarySizing.ContentHeight);

        try
        {
            window.Close();
            Assert.True(window.IsVisible);
            Assert.Equal(Localization.Format("Window.SaveFailedTitle", originalMessage), window.Title);

            Localization.SetLanguage("en-US");

            Assert.Equal(originalMessage, failure.Message);
            Assert.Equal(Localization.Get("Layout.Validation.ObjectRequired"), LocalizedExceptionText.Get(failure));
            Assert.Equal(Localization.Format("Window.SaveFailedTitle", LocalizedExceptionText.Get(failure)), window.Title);
            Assert.Equal(Localization.Get("Layout.CustomPresetMissing"), LocalizedExceptionText.Get(nameFailure));
            Assert.Equal(LocalizedExceptionText.Get(nameFailure), LayoutValidationFailure.GetDisplayMessage(nameFailure));
            Assert.Equal("name", nameFailure.ParamName);
        }
        finally
        {
            canClose = true;
            window.Close();
            owner.Close();
        }
    }
}
