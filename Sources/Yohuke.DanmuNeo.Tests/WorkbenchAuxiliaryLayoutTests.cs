using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Lucide.Avalonia;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证辅助窗口按实际内容调整高度、限制屏幕范围并保持滚动和模态关闭行为。
/// </summary>
public class WorkbenchAuxiliaryLayoutTests
{
    /// <summary>
    /// 新建工作区及直播间使用填满客户区的表面，窗口高度随表单展开和搜索结果调整。
    /// </summary>
    [AvaloniaFact]
    public async Task AddDialogsFillClientAreaAndFollowFormHeight()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            context.ViewModel.OpenAddWorkspaceDialog();
            var addWorkspace = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("add"));
            PumpLayout(addWorkspace);
            Assert.Equal(SizeToContent.Height, addWorkspace.SizeToContent);
            Assert.False(addWorkspace.CanResize);
            Assert.InRange(addWorkspace.ClientSize.Height - GetTitleBarHeight(addWorkspace), 120, 349);
            AssertSurfaceFillsClientArea(addWorkspace);
            SaveFrame(addWorkspace, "surface-add-workspace.png");
            context.ViewModel.CloseAddDialog();
            Assert.Null(workbench.Windows.FindAuxiliary("add"));

            context.ViewModel.OpenAddRoomDialog(context.ViewModel.SelectedWorkspace);
            var addRoom = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("add"));
            PumpLayout(addRoom);
            var initialHeight = addRoom.ClientSize.Height;
            AssertSurfaceFillsClientArea(addRoom);
            SaveFrame(addRoom, "surface-add-room.png");
            var expander = addRoom.GetVisualDescendants().OfType<Expander>().Single();
            expander.IsExpanded = true;
            PumpLayout(addRoom);
            Assert.True(addRoom.ClientSize.Height > initialHeight + 60);

            for (var index = 0; index < 12; index++)
            {
                context.ViewModel.LiveRoomSearchResults.Add(new()
                {
                    RoomId = (200 + index).ToString(), OwnerName = $"测试主播 {index}", Title = "直播间搜索结果"
                });
            }

            PumpLayout(addRoom);
            AssertSurfaceFillsClientArea(addRoom);
            Assert.True(addRoom.ClientSize.Height <= addRoom.MaxHeight + 0.5);
            var results = addRoom.GetVisualDescendants().OfType<ItemsControl>()
                .Single(control => control.Name == "LiveRoomSearchResultsControl");
            var resultsScroll = results.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(resultsScroll.Extent.Height > resultsScroll.Viewport.Height);
            Assert.True(resultsScroll.Extent.Width <= resultsScroll.Viewport.Width + 0.5);
            AssertFullyVisible(addRoom.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "取消")), addRoom);
            SaveFrame(addRoom, "surface-add-room-expanded.png");
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 设置和歌词库窗口铺满客户区，保留主要操作并移除页内关闭按钮，歌词库使用统一内容边距。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResizableDialogSurfacesFillClientArea(bool lyricLibrary)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);

            if (lyricLibrary)
            {
                workbench.Panels.Lyrics.OpenLyricLibraryPickerCommand.Execute(null);
            }
            else
            {
                workbench.MenuCatalog.SettingsCommand.Execute(null);
            }

            var key = lyricLibrary ? "lyric-library" : "settings";
            var dialog = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary(key));
            PumpLayout(dialog);
            Assert.Equal(SizeToContent.Manual, dialog.SizeToContent);
            Assert.True(dialog.CanResize);
            AssertSurfaceFillsClientArea(dialog);
            AssertDialogHeaderAndContentInset(dialog, lyricLibrary);
            SaveFrame(dialog, $"surface-{key}.png");
            dialog.Width = 900;
            dialog.Height = 680;
            PumpLayout(dialog);
            AssertSurfaceFillsClientArea(dialog);
            AssertDialogHeaderAndContentInset(dialog, lyricLibrary);
            SaveFrame(dialog, $"surface-{key}-resized.png");
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 已有独立歌词编辑窗在默认及放大尺寸下均铺满表面，编辑区和底部操作仍在窗口内。
    /// </summary>
    [AvaloniaFact]
    public void LyricEditorSurfaceFillsResizedClientArea()
    {
        var owner = new Window { Width = 1000, Height = 740 };
        var editor = new LyricEditDialogWindow(new() { Title = "歌词表面测试", LyricText = "[00:01]测试歌词" });

        try
        {
            owner.Show();
            editor.Show(owner);
            PumpLayout(editor);
            AssertSurfaceFillsClientArea(editor);
            editor.Width = 1060;
            editor.Height = 720;
            PumpLayout(editor);
            AssertSurfaceFillsClientArea(editor);
            AssertFullyVisible(editor.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "保存")), editor);
            SaveFrame(editor, "surface-lyric-editor-resized.png");
        }
        finally
        {
            editor.Close();
            owner.Close();
        }
    }

    /// <summary>
    /// 检查中、更新结果和长错误文案在同一窗口内增减高度，短消息不再保留固定空白。
    /// </summary>
    [AvaloniaFact]
    public async Task UpdateWindowFollowsCheckingResultAndMessageHeight()
    {
        using var context = new AppUpdateTestContext();
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            var check = context.ViewModel.CheckForUpdatesManuallyAsync();
            var update = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("update"));
            PumpLayout(update);
            var view = update.GetVisualDescendants().OfType<AppUpdateDialogView>().Single();
            var message = view.FindControl<TextBlock>("UpdateMessageText")!;
            var checkingHeight = update.ClientSize.Height;
            Assert.Equal(SizeToContent.Height, update.SizeToContent);
            Assert.False(update.CanResize);
            Assert.InRange(checkingHeight - GetTitleBarHeight(update), 80, 299);
            AssertSurfaceFillsClientArea(update);
            Assert.Equal("正在检查更新…", message.Text);
            AssertFullyVisible(view.FindControl<Button>("DismissUpdateButton")!, update);
            Assert.False(workbench.Windows.IsModal);
            Assert.True(main.IsEnabled);
            SaveFrame(update, "update-checking.png");

            context.UpdateService.Response.SetResult(context.Update);
            await check;
            PumpLayout(update);
            var resultHeight = update.ClientSize.Height;
            Assert.True(resultHeight > checkingHeight + 20);
            AssertFullyVisible(view.FindControl<Button>("OpenReleaseButton")!, update);
            SaveFrame(update, "update-result.png");

            context.ViewModel.AvailableUpdate = null;
            context.ViewModel.UpdateCheckMessage = "检查更新失败：" + string.Concat(Enumerable.Repeat("网络连接暂时不可用，请稍后重试。", 6));
            PumpLayout(update);
            Assert.True(update.ClientSize.Height > resultHeight);
            Assert.True(message.Bounds.Height >= message.TextLayout.Height - 0.5);
            AssertFullyVisible(message, update);
            AssertFullyVisible(view.FindControl<Button>("DismissUpdateButton")!, update);

            context.ViewModel.UpdateCheckMessage = "当前已是最新版本";
            PumpLayout(update);
            Assert.InRange(update.ClientSize.Height, 80, checkingHeight + 1);
            view.FindControl<Button>("DismissUpdateButton")!.Command!.Execute(null);
            Assert.False(context.ViewModel.IsUpdateDialogOpen);
            Assert.Null(workbench.Windows.FindAuxiliary("update"));
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 浏览器错误会扩展窗口，超长消息受屏幕工作区限制且可滚动到操作按钮，缩短后恢复紧凑高度。
    /// </summary>
    [AvaloniaFact]
    public async Task UpdateWindowScrollsLongContentAndResizesAfterLaunchFailure()
    {
        using var context = new AppUpdateTestContext();
        context.ViewModel.IsUpdateDialogOpen = true;
        context.ViewModel.AvailableUpdate = context.Update;
        var owner = new Window { Width = 1000, Height = 700 };
        using var windows = new WorkbenchWindowRegistry(owner);

        try
        {
            owner.Show();
            windows.RegisterBusinessWindow(owner);
            var view = new AppUpdateDialogView(_ => Task.FromResult(false)) { DataContext = context.ViewModel };
            var update = windows.OpenAuxiliary("update", "检查更新", () => view,
                beforeClose: () =>
                {
                    context.ViewModel.CloseUpdateDialog();
                    return Task.CompletedTask;
                },
                width: 620, sizing: WorkbenchAuxiliarySizing.ContentHeight);
            PumpLayout(update);
            var resultHeight = update.ClientSize.Height;
            var release = view.FindControl<Button>("OpenReleaseButton")!;
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(release.Command).ExecuteAsync(null);
            PumpLayout(update);
            Assert.True(update.ClientSize.Height > resultHeight);
            AssertFullyVisible(view.FindControl<TextBlock>("LaunchErrorText")!, update);

            context.ViewModel.UpdateCheckMessage =
                "检查更新失败：" + string.Concat(Enumerable.Repeat("服务器返回的错误详情，请检查网络连接后重试。", 300));
            PumpLayout(update);
            var scroll = update.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(control => ReferenceEquals(control.Content, view));
            var screen = update.Screens.ScreenFromWindow(update)!;
            Assert.True(update.ClientSize.Height <= screen.WorkingArea.Height / screen.Scaling);
            Assert.True(update.ClientSize.Height <= update.MaxHeight + 0.5);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5);
            scroll.ScrollToEnd();
            PumpLayout(update);
            Assert.True(scroll.Offset.Y > 0);
            AssertFullyVisible(view.FindControl<Button>("DismissUpdateButton")!, update);
            AssertFullyVisible(release, update);

            context.ViewModel.UpdateCheckMessage = "发现新版本";
            PumpLayout(update);
            Assert.True(update.ClientSize.Height - GetTitleBarHeight(update) < 300);
            Assert.Equal(0, scroll.Offset.Y);
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 0.5);
            update.Close();
            Assert.False(context.ViewModel.IsUpdateDialogOpen);
            Assert.True(owner.IsEnabled);
        }
        finally
        {
            windows.Dispose();
            owner.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 设置上的长删除确认可滚动到取消按钮，关闭确认后恢复设置交互，并仍由原入口保存关闭设置。
    /// </summary>
    [AvaloniaFact]
    public async Task LongNestedConfirmationKeepsSettingsOwnerAndSaveClose()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            await context.ViewModel.AddWorkspaceAsync();
            Assert.Equal(2, context.ViewModel.Workspaces.Count);
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.MenuCatalog.SettingsCommand.Execute(null);
            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("settings"));
            PumpLayout(settings);
            AssertSurfaceFillsClientArea(settings);
            Assert.Equal(SizeToContent.Manual, settings.SizeToContent);
            Assert.True(settings.CanResize);
            Assert.True(context.ViewModel.IsSettingsOpen);
            Assert.Same(settings, workbench.Windows.CurrentDialogOwner);
            var workspace = context.ViewModel.Workspaces[0];
            workspace.Name = "需要删除的测试工作区";
            context.ViewModel.OpenDeleteWorkspaceDialog(workspace);
            Assert.True(context.ViewModel.IsConfirmDeleteDialogOpen);
            var confirm = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("confirm"));
            PumpLayout(confirm);
            AssertSurfaceFillsClientArea(confirm);
            SaveFrame(confirm, "surface-confirm-delete.png");
            Assert.Contains(workspace.Name, context.ViewModel.DeleteDialogDescription);
            var description = context.ViewModel.DeleteDialogDescription + Environment.NewLine +
                              string.Join(Environment.NewLine, Enumerable.Range(1, 300)
                                  .Select(index => $"第 {index} 项配置：删除后将不再保留该房间的工作区配置，请确认已完成所需备份。"));
            context.ViewModel.DeleteDialogDescription = description;
            PumpLayout(confirm);
            Assert.Same(settings, confirm.Owner);
            Assert.False(main.IsEnabled);
            Assert.False(settings.IsEnabled);
            Assert.True(confirm.IsEnabled);
            Assert.Equal(SizeToContent.Height, confirm.SizeToContent);
            var scroll = confirm.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(control => control.Content is ConfirmDeleteDialogView);
            var view = confirm.GetVisualDescendants().OfType<ConfirmDeleteDialogView>().Single();
            var textBlocks = view.GetVisualDescendants().OfType<TextBlock>().ToArray();
            var diagnostics =
                $"workspace.Name.Length={workspace.Name.Length}; description.Length={description.Length}; " +
                $"confirm.ClientSize={confirm.ClientSize}; MaxHeight={confirm.MaxHeight}; " +
                $"scroll.Extent={scroll.Extent}; scroll.Viewport={scroll.Viewport}; " +
                $"view.Bounds={view.Bounds}; view.DesiredSize={view.DesiredSize}; " +
                string.Join("; ", textBlocks.Select(text =>
                    $"Text.Length={text.Text?.Length}, Bounds={text.Bounds}, DesiredSize={text.DesiredSize}, TextLayout.Height={text.TextLayout.Height}"));
            Assert.True(textBlocks.Any(text => text.Text == description), diagnostics);
            var descriptionText = textBlocks.Single(text => text.Text == description);
            Assert.True(descriptionText.Bounds.Height >= descriptionText.TextLayout.Height - 0.5, diagnostics);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height, diagnostics);
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5, diagnostics);
            scroll.ScrollToEnd();
            PumpLayout(confirm);
            var cancel = view.FindControl<Button>("CancelDeleteButton")!;
            AssertFullyVisible(cancel, confirm);
            cancel.Command!.Execute(null);
            Assert.False(context.ViewModel.IsConfirmDeleteDialogOpen);
            Assert.Null(workbench.Windows.FindAuxiliary("confirm"));
            Assert.True(settings.IsEnabled);
            Assert.False(main.IsEnabled);
            Assert.True(workbench.Windows.IsModal);

            context.ViewModel.UseWindowMenuOnMac = true;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            settings.Closed += (_, _) => closed.TrySetResult();
            Assert.Single(settings.GetVisualDescendants().OfType<SettingsDialogView>()).CloseSettingsCommand.Execute(null);
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(context.StateService.Load().Settings.UseWindowMenuOnMac);
            Assert.False(workbench.Windows.IsModal);
            Assert.True(main.IsEnabled);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 关于和命名窗口保持内容高度，命名校验错误扩展窗口，布局管理则保留用户可调尺寸。
    /// </summary>
    [AvaloniaFact]
    public async Task AboutAndNamingFitContentWhileLayoutManagementRemainsResizable()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            ExecuteMenu(workbench, "help", "about");
            var about = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("about"));
            PumpLayout(about);
            Assert.Equal(SizeToContent.Height, about.SizeToContent);
            Assert.InRange(about.ClientSize.Height - GetTitleBarHeight(about), 120, 329);
            AssertSurfaceFillsClientArea(about);
            AssertFullyVisible(about.GetVisualDescendants().OfType<Button>().Single(), about);
            SaveFrame(about, "surface-about.png");
            about.Close();

            ExecuteMenu(workbench, "layouts", "layout-save-as");
            var naming = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("save-layout"));
            PumpLayout(naming);
            var initialHeight = naming.ClientSize.Height;
            Assert.InRange(initialHeight - GetTitleBarHeight(naming), 80, 279);
            Assert.Equal(SizeToContent.Height, naming.SizeToContent);
            AssertSurfaceFillsClientArea(naming);
            SaveFrame(naming, "surface-save-layout.png");
            var save = naming.GetVisualDescendants().OfType<Button>().Single();
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpLayout(naming);
            Assert.True(naming.ClientSize.Height > initialHeight);
            AssertFullyVisible(save, naming);
            Assert.False(main.IsEnabled);
            naming.Close();
            Assert.True(main.IsEnabled);

            workbench.Layouts.SaveAs("布局表面测试");
            ExecuteMenu(workbench, "layouts", "layout-manage");
            var manager = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("manage-layouts"));
            Assert.Equal(SizeToContent.Manual, manager.SizeToContent);
            Assert.True(manager.CanResize);
            manager.Height = 480;
            PumpLayout(manager);
            Assert.Equal(480, manager.ClientSize.Height);
            AssertSurfaceFillsClientArea(manager);
            SaveFrame(manager, "surface-manage-layouts.png");
            manager.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "删除")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var delete = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("delete-layout"));
            PumpLayout(delete);
            AssertSurfaceFillsClientArea(delete);
            Assert.Same(manager, delete.Owner);
            SaveFrame(delete, "surface-delete-layout.png");
            delete.Close();
            manager.Close();
            Assert.False(workbench.Windows.IsModal);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 用户将辅助窗拖过当前屏幕边缘时不强制回推，使窗口能够继续进入相邻屏幕。
    /// </summary>
    [AvaloniaFact]
    public void AuxiliaryWindowsAllowDraggingAcrossCurrentScreenEdges()
    {
        var owner = new Window { Width = 1000, Height = 700 };
        using var windows = new WorkbenchWindowRegistry(owner);

        try
        {
            owner.Show();
            windows.RegisterBusinessWindow(owner);
            var window = windows.OpenAuxiliary("movement", "移动窗口", () => new Border { Height = 100 },
                width: 560, sizing: WorkbenchAuxiliarySizing.ContentHeight);
            PumpLayout(window);
            var screen = window.Screens.ScreenFromWindow(window)!;
            var halfWidth = (int)Math.Ceiling((window.FrameSize ?? window.ClientSize).Width * screen.Scaling / 2);
            var positions = new[]
            {
                new PixelPoint(screen.WorkingArea.X - halfWidth, screen.WorkingArea.Y + 100),
                new PixelPoint(screen.WorkingArea.Right - halfWidth, screen.WorkingArea.Y + 100)
            };

            foreach (var position in positions)
            {
                window.Position = position;
                PumpLayout(window);
                Assert.Equal(position, window.Position);
                Assert.True(window.IsEnabled);
                Assert.True(window.ClientSize.Height <= window.MaxHeight);
            }
        }
        finally
        {
            windows.Dispose();
            owner.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void ExecuteMenu(WorkbenchSession workbench, string group, string entry)
    {
        workbench.MenuCatalog.Entries.Single(item => item.Id == group).Children!
            .Single(item => item.Id == entry).Command!.Execute(null);
    }

    private static void AssertFullyVisible(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible);
        var bounds = new Rect(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        Assert.InRange(bounds.Left, -0.5, window.ClientSize.Width);
        Assert.InRange(bounds.Right, bounds.Left + 1, window.ClientSize.Width + 0.5);
        Assert.InRange(bounds.Top, -0.5, window.ClientSize.Height);
        Assert.InRange(bounds.Bottom, bounds.Top + 1, window.ClientSize.Height + 0.5);
    }

    private static void AssertSurfaceFillsClientArea(Window window)
    {
        var surface = window.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "DialogSurface");
        var bounds = new Rect(surface.TranslatePoint(default, window)!.Value, surface.Bounds.Size);
        var diagnostics = $"window.ClientSize={window.ClientSize}; surface.Bounds={bounds}; " +
                          $"surface.Margin={surface.Margin}; surface.CornerRadius={surface.CornerRadius}";
        Assert.Equal(new Thickness(0), surface.Margin);
        Assert.Equal(new CornerRadius(0), surface.CornerRadius);
        Assert.True(double.IsPositiveInfinity(surface.MaxWidth), diagnostics);
        Assert.InRange(bounds.Left, -0.5, 0.5);
        var titleBarHeight = GetTitleBarHeight(window);
        Assert.InRange(bounds.Top, titleBarHeight - 0.5, titleBarHeight + 0.5);
        Assert.True(Math.Abs(bounds.Width - window.ClientSize.Width) <= 0.5, diagnostics);
        Assert.True(Math.Abs(bounds.Bottom - window.ClientSize.Height) <= 0.5, diagnostics);
    }

    private static void AssertDialogHeaderAndContentInset(Window window, bool lyricLibrary)
    {
        var surface = window.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "DialogSurface");
        var content = Assert.IsType<Grid>(surface.Child);
        Control header;

        if (lyricLibrary)
        {
            header = content.Children.OfType<Grid>().First();
            var bounds = new Rect(content.TranslatePoint(default, window)!.Value, content.Bounds.Size);
            Assert.InRange(bounds.Left, 11.5, 12.5);
            var titleBarHeight = GetTitleBarHeight(window);
            Assert.InRange(bounds.Top, titleBarHeight + 11.5, titleBarHeight + 12.5);
            Assert.InRange(window.ClientSize.Width - bounds.Right, 11.5, 12.5);
            Assert.InRange(window.ClientSize.Height - bounds.Bottom, 11.5, 12.5);
            var import = header.GetVisualDescendants().OfType<LyricLocalImportButton>().Single();
            Assert.True(import.IsCompact && import.LoadToCurrentRoom);
            AssertFullyVisible(import, window);
        }
        else
        {
            header = content.Children.OfType<Border>()
                .Single(border => border.Classes.Contains("settingsDialogHeader"));
            var view = Assert.Single(window.GetVisualDescendants().OfType<SettingsDialogView>());
            var save = Assert.Single(header.GetVisualDescendants().OfType<Button>(),
                button => ReferenceEquals(button.Command, view.CloseSettingsCommand));
            Assert.Contains(save.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "保存");
            AssertFullyVisible(save, window);
        }

        Assert.DoesNotContain(header.GetVisualDescendants().OfType<LucideIcon>(),
            icon => icon.Kind == LucideIconKind.X);
    }

    private static double GetTitleBarHeight(Window window)
    {
        return Assert.Single(window.GetVisualDescendants().OfType<WindowTitleBar>()).Bounds.Height;
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void SaveFrame(Window window, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_DOCK_WINDOW_QA_OUTPUT") ??
                        Environment.GetEnvironmentVariable("YOHUKE_DOCK_QA_OUTPUT");

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, fileName));
        }
    }
}
