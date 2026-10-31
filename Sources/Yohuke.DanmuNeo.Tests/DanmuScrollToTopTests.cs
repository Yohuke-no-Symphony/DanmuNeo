using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证弹幕标题栏回顶按钮、阅读锚点复位和可配置快捷键。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class DanmuScrollToTopTests
{
    /// <summary>
    /// 停靠和浮动面板的标题栏按钮均撤销排队的阅读定位，并恢复最新弹幕跟随。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HeaderButtonReturnsToTopAndResumesFollowing(bool floating, bool compact)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.IsCompactDanmuDisplay = compact;
        PopulateDanmu(fixture.Room);
        var main = new MainWindow { DataContext = fixture.ViewModel, Width = 1100, Height = 800 };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.ShowPanel(WorkbenchPanelIds.DANMU);
            Window host = main;

            if (floating)
            {
                workbench.Layouts.FloatPanel(WorkbenchPanelIds.DANMU);
                host = Assert.Single(workbench.Layouts.FloatingWindows);
            }

            PumpLayout(main, host);
            var scroll = workbench.Panels.Danmu.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            var button = GetHeaderButton(host);
            var chrome = button.FindAncestorOfType<WorkbenchToolChromeControl>();
            Assert.NotNull(chrome);
            Assert.Same(workbench.Layouts.PanelAdapters[WorkbenchPanelIds.DANMU].Owner, chrome.DataContext);
            Assert.True(button.IsEffectivelyVisible);
            Assert.Same(workbench.Panels.Danmu.ScrollToTopCommand, button.Command);
            Assert.True(button.IsEffectivelyEnabled);
            Assert.Equal(22, button.Bounds.Width);
            Assert.Equal(22, button.Bounds.Height);
            var icon = Assert.IsAssignableFrom<Control>(button.Content);
            Assert.InRange(icon.Bounds.Width, 16, button.Bounds.Width);
            Assert.InRange(icon.Bounds.Height, 16, button.Bounds.Height);

            scroll.Offset = new(0, 180);
            PumpLayout(host);
            Assert.True(scroll.Offset.Y > 1);
            fixture.Room.AddDanmu(new() { RoomId = "100", Content = new string('长', 180) });
            ClickButton(host, button);
            PumpLayout(host);

            Assert.Equal(0, scroll.Offset.Y);
            fixture.Room.AddDanmu(new() { RoomId = "100", Content = "回顶后继续跟随最新弹幕" });
            PumpLayout(host);
            Assert.Equal(0, scroll.Offset.Y);

            scroll.Offset = new(0, 180);
            PumpLayout(host);
            fixture.Room.AddDanmu(new() { RoomId = "100", Content = "再次翻阅时保留阅读位置" });
            PumpLayout(host);
            Assert.True(scroll.Offset.Y > 1);

            fixture.ViewModel.SelectedWorkspace!.SelectedRoom = null;
            PumpLayout(host);
            Assert.False(button.IsEffectivelyEnabled);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 标题栏只展示当前弹幕标签的按钮，切换标签及浮动重挂后仍可执行。
    /// </summary>
    [AvaloniaFact]
    public async Task HeaderActionTracksActiveTabAndDockMigration()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        PopulateDanmu(fixture.Room);
        var main = new MainWindow { DataContext = fixture.ViewModel, Width = 1100, Height = 800 };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.ShowPanel(WorkbenchPanelIds.DANMU);
            PumpLayout(main);
            var command = GetHeaderButton(main).Command;
            Assert.NotNull(command);

            workbench.Layouts.ShowPanel(WorkbenchPanelIds.HISTORY);
            PumpLayout(main);
            Assert.DoesNotContain(main.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "DanmuScrollToTopButton" && button.IsEffectivelyVisible);

            workbench.Layouts.ShowPanel(WorkbenchPanelIds.DANMU);
            PumpLayout(main);
            Assert.Same(command, GetHeaderButton(main).Command);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.DANMU);
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            PumpLayout(main, floating);
            var button = GetHeaderButton(floating);
            Assert.Same(command, button.Command);
            var scroll = workbench.Panels.Danmu.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            scroll.Offset = new(0, 180);
            PumpLayout(floating);
            Assert.True(scroll.Offset.Y > 1);

            ClickButton(floating, button);
            PumpLayout(floating);

            Assert.Equal(0, scroll.Offset.Y);
            Assert.True(workbench.Layouts.IsVisible(WorkbenchPanelIds.DANMU));
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 默认快捷键在主窗及输入浮窗内均可回顶，不改变同传草稿、光标与输入焦点。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShortcutReturnsToTopFromComposerWithoutChangingDraft(bool floatingComposer)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        PopulateDanmu(fixture.Room);
        var main = new MainWindow { DataContext = fixture.ViewModel, Width = 1100, Height = 800 };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.ShowPanel(WorkbenchPanelIds.DANMU);
            Window host = main;

            if (floatingComposer)
            {
                workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
                host = Assert.Single(workbench.Layouts.FloatingWindows);
            }

            PumpLayout(main, host);
            host.Activate();
            workbench.Panels.FocusInputDraft();
            PumpLayout(main, host);
            var input = workbench.Panels.Composer.FindControl<TextBox>("InputDraftTextBox")!;
            input.Text = "保留同传草稿和输入位置";
            input.CaretIndex = 3;
            var scroll = workbench.Panels.Danmu.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            scroll.Offset = new(0, 180);
            PumpLayout(main, host);
            Assert.True(scroll.Offset.Y > 1);
            Assert.True(input.IsFocused);
            fixture.Room.AddDanmu(new() { RoomId = "100", Content = "快捷键之前排队的锚点定位" });

            host.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Alt);
            host.KeyReleaseQwerty(PhysicalKey.Home, RawInputModifiers.Alt);
            PumpLayout(main, host);

            Assert.Equal(0, scroll.Offset.Y);
            Assert.Equal("保留同传草稿和输入位置", input.Text);
            Assert.Equal(3, input.CaretIndex);
            Assert.True(input.IsFocused);
            fixture.Room.AddDanmu(new() { RoomId = "100", Content = "快捷键回顶后跟随" });
            PumpLayout(main, host);
            Assert.Equal(0, scroll.Offset.Y);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 回顶快捷键遵循改绑、禁用和冲突检测，并在快捷键录制期间保持停用。
    /// </summary>
    [AvaloniaFact]
    public async Task ShortcutHonorsRebindingDisabledConflictingAndRecordingStates()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        PopulateDanmu(fixture.Room);
        var main = new MainWindow { DataContext = fixture.ViewModel, Width = 1100, Height = 800 };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.ShowPanel(WorkbenchPanelIds.DANMU);
            workbench.Panels.FocusInputDraft();
            PumpLayout(main);
            var scroll = workbench.Panels.Danmu.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            var binding = Assert.Single(fixture.ViewModel.Settings.ShortcutBindings,
                item => item.ActionKey == "danmu.scrollToTop");
            binding.GestureText = "Ctrl+Shift+T";
            scroll.Offset = new(0, 180);
            PumpLayout(main);

            main.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.Alt);
            main.KeyReleaseQwerty(PhysicalKey.Home, RawInputModifiers.Alt);
            PumpLayout(main);
            Assert.True(scroll.Offset.Y > 1);

            var modifiers = RawInputModifiers.Control | RawInputModifiers.Shift;
            main.KeyPressQwerty(PhysicalKey.T, modifiers);
            main.KeyReleaseQwerty(PhysicalKey.T, modifiers);
            PumpLayout(main);
            Assert.Equal(0, scroll.Offset.Y);

            scroll.Offset = new(0, 180);
            PumpLayout(main);
            binding.IsEnabled = false;
            main.KeyPressQwerty(PhysicalKey.T, modifiers);
            main.KeyReleaseQwerty(PhysicalKey.T, modifiers);
            PumpLayout(main);
            Assert.True(scroll.Offset.Y > 1);

            binding.IsEnabled = true;
            var conflicting = fixture.ViewModel.Settings.ShortcutBindings.Single(item =>
                item.ActionKey == ShortcutActionKeys.INPUT_CLEAR_DRAFT);
            conflicting.GestureText = binding.GestureText;
            main.KeyPressQwerty(PhysicalKey.T, modifiers);
            main.KeyReleaseQwerty(PhysicalKey.T, modifiers);
            PumpLayout(main);
            Assert.True(scroll.Offset.Y > 1);

            conflicting.IsEnabled = false;
            var recording = fixture.ViewModel.ShortcutBindingGroups.SelectMany(group => group.Bindings).First();
            fixture.ViewModel.BeginRecordShortcut(recording);
            main.KeyPressQwerty(PhysicalKey.T, modifiers);
            main.KeyReleaseQwerty(PhysicalKey.T, modifiers);
            PumpLayout(main);
            Assert.True(scroll.Offset.Y > 1);
            fixture.ViewModel.CancelShortcutRecording();
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 已创建的标题栏按钮和快捷键设置在切换语言时即时刷新，不重建命令或按钮。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeaderButtonAndShortcutLabelsRefreshWithLanguage(bool floating)
    {
        using var language = new LocalizationTestScope();
        using var fixture = new LiveRoomFeedViewTestContext();
        var main = new MainWindow { DataContext = fixture.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.ShowPanel(WorkbenchPanelIds.DANMU);
            Window host = main;

            if (floating)
            {
                workbench.Layouts.FloatPanel(WorkbenchPanelIds.DANMU);
                host = Assert.Single(workbench.Layouts.FloatingWindows);
            }

            PumpLayout(main, host);
            var button = GetHeaderButton(host);
            var group = fixture.ViewModel.ShortcutBindingGroups.Single(item =>
                item.Bindings.Any(binding => binding.ActionKey == ShortcutActionKeys.DANMU_SCROLL_TO_TOP));
            var binding = Assert.Single(group.Bindings);

            foreach (var languageId in new[] { "zh-CN", "en-US" })
            {
                Localization.SetLanguage(languageId);
                PumpLayout(main, host);

                Assert.Same(button, GetHeaderButton(host));
                Assert.Equal(languageId == "zh-CN" ? "回到顶部" : "Back to top", ToolTip.GetTip(button));
                Assert.Equal(ToolTip.GetTip(button), AutomationProperties.GetName(button));
                Assert.Equal(languageId == "zh-CN" ? "弹幕" : "Danmu", group.Name);
                Assert.Equal(languageId == "zh-CN" ? "弹幕回到顶部" : "Scroll danmu to top", binding.Name);
                Assert.Same(workbench.Panels.Danmu.ScrollToTopCommand, button.Command);
            }
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    private static void PopulateDanmu(LiveRoomTabViewModel room)
    {
        room.DanmuItems.Clear();

        for (var index = 0; index < 80; index++)
        {
            room.DanmuItems.Add(new()
            {
                RoomId = "100", UserUid = "30", UserName = $"用户 {index}", Content = $"历史弹幕 {index}"
            });
        }
    }

    private static Button GetHeaderButton(Window window)
    {
        return Assert.Single(window.GetVisualDescendants().OfType<Button>(),
            button => button.Name == "DanmuScrollToTopButton" && button.IsEffectivelyVisible);
    }

    private static void ClickButton(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void PumpLayout(params Window[] windows)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            Dispatcher.UIThread.RunJobs();

            foreach (var window in windows)
            {
                window.UpdateLayout();
            }
        }
    }
}
