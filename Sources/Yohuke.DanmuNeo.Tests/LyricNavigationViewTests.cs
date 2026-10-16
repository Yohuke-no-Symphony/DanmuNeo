using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 歌词下一句按钮、双击和快捷键的真实界面回归测试。
/// </summary>
public class LyricNavigationViewTests
{
    /// <summary>
    /// 下一句按钮一次略过连续空行，末尾只剩空行时禁用。
    /// </summary>
    [AvaloniaFact]
    public async Task NextButtonSkipsEmptyRowsAndDisablesAtTrailingBlanks()
    {
        using var context = new LyricNavigationViewTestContext();
        context.Room.LyricInput = "[00:01]当前句\n[00:02]\n[00:03] \t\n[00:04]有效句\n[00:05]";
        context.Room.ApplyLyric();
        var window = CreateWindow(context, false);

        try
        {
            var view = await ShowLyricsAsync(window);
            var button = Assert.IsType<Button>(view.FindControl<Button>("NextLyricButton"));
            Assert.True(button.IsEffectivelyEnabled);

            Click(window, button);

            Assert.Equal(["【♪有效句】"], context.SendService.SentMessages);
            Assert.Same(context.Room.Lyrics[3], context.Room.ActiveLyricLine);
            Assert.False(button.IsEffectivelyEnabled);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 替换规则不再清空下一句时，关闭设置后立即恢复按钮可用状态。
    /// </summary>
    [AvaloniaFact]
    public async Task NextButtonRefreshesAfterReplacementSettingsChange()
    {
        using var context = new LyricNavigationViewTestContext();
        context.ViewModel.Settings.ShieldReplacementRules =
        [
            new()
            {
                SourceText = "删除",
                ReplacementText = ""
            }
        ];
        context.Room.LyricInput = "[00:01]当前句\n[00:02]删除";
        context.Room.ApplyLyric();
        var window = CreateWindow(context, false);

        try
        {
            var view = await ShowLyricsAsync(window);
            var button = Assert.IsType<Button>(view.FindControl<Button>("NextLyricButton"));
            Assert.False(button.IsEffectivelyEnabled);

            context.ViewModel.Settings.ShieldReplacementRules[0].ReplacementText = "有效句";
            await context.ViewModel.CloseSettingsAsync();
            PumpLayout(window);

            Assert.True(button.IsEffectivelyEnabled);
            Click(window, button);
            Assert.Equal(["【♪有效句】"], context.SendService.SentMessages);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 下一句按钮发送目标行，最后一句和空列表禁用按钮。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(300d)]
    [InlineData(480d)]
    public async Task NextButtonSendsTargetAndFitsToolColumn(double toolWidth)
    {
        using var context = new LyricNavigationViewTestContext();
        context.ViewModel.Settings.WorkspaceToolColumnWidth = toolWidth;
        var window = CreateWindow(context, false);

        try
        {
            var view = await ShowLyricsAsync(window);
            var button = Assert.IsType<Button>(view.FindControl<Button>("NextLyricButton"));
            Assert.True(button.IsEffectivelyEnabled);
            var buttons = ((Grid)button.Parent!).Children.OfType<Button>().ToList();

            for (var index = 1; index < buttons.Count; index++)
            {
                Assert.True(buttons[index - 1].Bounds.Right <= buttons[index].Bounds.Left);
            }

            Click(window, button);
            Assert.Equal(["【♪第二句】"], context.SendService.SentMessages);
            Assert.Same(context.Room.Lyrics[1], context.Room.ActiveLyricLine);
            Assert.False(context.Room.IsLyricAutoSending);
            SavePreview(window, toolWidth);
            Click(window, button);
            Assert.Equal(["【♪第二句】", "【♪第三句】"], context.SendService.SentMessages);
            Assert.False(button.IsEffectivelyEnabled);
            context.Room.ClearLyric();
            PumpLayout(window);
            Assert.False(button.IsEffectivelyEnabled);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 暂停时真实双击定位并发送被点击的歌词，保持暂停状态。
    /// </summary>
    [AvaloniaFact]
    public async Task DoubleClickSendsClickedLineWhilePaused()
    {
        using var context = new LyricNavigationViewTestContext();
        var window = CreateWindow(context, false);

        try
        {
            var view = await ShowLyricsAsync(window);
            var list = view.FindControl<ListBox>("LyricListBox")!;
            var row = list.ContainerFromIndex(1)!;
            var point = GetCenter(row, window);
            window.MouseMove(point);

            for (var index = 0; index < 2; index++)
            {
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }

            PumpLayout(window);
            Assert.Equal(["【♪第二句】"], context.SendService.SentMessages);
            Assert.Same(context.Room.Lyrics[1], context.Room.ActiveLyricLine);
            Assert.Equal(10, context.Room.LyricPlaybackPositionSeconds);
            Assert.False(context.Room.IsLyricAutoSending);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 主窗口与独立工作页面中，一次按压只前进一句，松开后可以再次触发。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NextShortcutSuppressesRepeatedKeyDown(bool useMainWindow)
    {
        using var context = new LyricNavigationViewTestContext();
        var window = CreateWindow(context, useMainWindow);

        try
        {
            var view = await ShowLyricsAsync(window);
            Assert.True(view.FindControl<ListBox>("LyricListBox")!.ContainerFromIndex(0)!.Focus());
            var modifiers = RawInputModifiers.Control | RawInputModifiers.Shift;
            window.KeyPressQwerty(PhysicalKey.ArrowDown, modifiers);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, modifiers);
            PumpLayout(window);
            Assert.Equal(["【♪第二句】"], context.SendService.SentMessages);
            Assert.Same(context.Room.Lyrics[1], context.Room.ActiveLyricLine);

            window.KeyReleaseQwerty(PhysicalKey.ArrowDown, modifiers);
            Press(window, PhysicalKey.ArrowDown, modifiers);
            Assert.Equal(["【♪第二句】", "【♪第三句】"], context.SendService.SentMessages);
            Assert.Same(context.Room.Lyrics[2], context.Room.ActiveLyricLine);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 自定义快捷键生效，默认手势不再发送歌词。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomNextShortcutReplacesDefault(bool useMainWindow)
    {
        using var context = new LyricNavigationViewTestContext();
        context.ViewModel.Settings.ShortcutBindings
            .Single(binding => binding.ActionKey == ShortcutActionKeys.LYRIC_SEEK_NEXT).GestureText = "Ctrl+N";
        var window = CreateWindow(context, useMainWindow);

        try
        {
            var view = await ShowLyricsAsync(window);
            Assert.True(view.FindControl<ListBox>("LyricListBox")!.ContainerFromIndex(0)!.Focus());
            Press(window, PhysicalKey.ArrowDown, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Empty(context.SendService.SentMessages);
            Press(window, PhysicalKey.N, RawInputModifiers.Control);
            Assert.Equal(["【♪第二句】"], context.SendService.SentMessages);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 禁用、冲突、编辑文本、设置和录制状态不触发歌词发送。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, "disabled")]
    [InlineData(true, "disabled")]
    [InlineData(false, "conflict")]
    [InlineData(true, "conflict")]
    [InlineData(false, "input")]
    [InlineData(true, "input")]
    [InlineData(false, "settings")]
    [InlineData(true, "settings")]
    [InlineData(false, "recording")]
    [InlineData(true, "recording")]
    public async Task NextShortcutHonorsInputAndSettingsGuards(bool useMainWindow, string mode)
    {
        using var context = new LyricNavigationViewTestContext();
        var binding = context.ViewModel.Settings.ShortcutBindings
            .Single(item => item.ActionKey == ShortcutActionKeys.LYRIC_SEEK_NEXT);

        if (mode == "disabled")
        {
            binding.IsEnabled = false;
        }
        else if (mode == "conflict")
        {
            context.ViewModel.Settings.ShortcutBindings
                .Single(item => item.ActionKey == ShortcutActionKeys.LYRIC_SEEK_FORWARD).GestureText = binding.GestureText;
        }

        var window = CreateWindow(context, useMainWindow);

        try
        {
            var view = await ShowLyricsAsync(window);

            if (mode == "input")
            {
                var input = window.GetVisualDescendants().OfType<DraftComposerPanelView>().Single().Input;
                input.Text = "正在编辑同传";
                input.Focus();
            }
            else
            {
                Assert.True(view.FindControl<ListBox>("LyricListBox")!.ContainerFromIndex(0)!.Focus());
            }

            if (mode == "settings")
            {
                context.ViewModel.IsSettingsOpen = true;
            }
            else if (mode == "recording")
            {
                var item = context.ViewModel.ShortcutBindingGroups.SelectMany(group => group.Bindings)
                    .Single(item => item.ActionKey == ShortcutActionKeys.LYRIC_SEEK_NEXT);
                context.ViewModel.BeginRecordShortcut(item);
            }

            Press(window, PhysicalKey.ArrowDown, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Empty(context.SendService.SentMessages);
            Assert.Same(context.Room.Lyrics[0], context.Room.ActiveLyricLine);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 播放时手动滚动暂停跟随后，下一句按钮和快捷键使目标行重新进入视野。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NextLineRestoresFollowingAfterManualScroll(bool clickButton)
    {
        using var context = new LyricNavigationViewTestContext();
        context.Room.LyricInput = string.Join('\n', Enumerable.Range(0, 80)
            .Select(index => $"[{index / 6:00}:{index % 6 * 10:00}]歌词 {index}"));
        context.Room.ApplyLyric();
        var window = CreateWindow(context, true);
        Task? playback = null;

        try
        {
            var view = await ShowLyricsAsync(window);
            var list = view.FindControl<ListBox>("LyricListBox")!;
            Assert.True(list.ContainerFromIndex(0)!.Focus());
            playback = context.Room.StartAutoLyricAsync();
            Press(window, PhysicalKey.PageDown, RawInputModifiers.None);
            context.Room.SeekLyricLineStart(context.Room.Lyrics[60]);
            PumpLayout(window);

            if (clickButton)
            {
                Click(window, view.FindControl<Button>("NextLyricButton")!);
            }
            else
            {
                Press(window, PhysicalKey.ArrowDown, RawInputModifiers.Control | RawInputModifiers.Shift);
            }

            PumpLayout(window);
            Assert.Same(context.Room.Lyrics[61], context.Room.ActiveLyricLine);
            var target = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(61));
            var position = target.TranslatePoint(new Point(0, 0), list)!.Value;
            Assert.True(position.Y >= 0);
            Assert.True(position.Y + target.Bounds.Height <= list.Bounds.Height);
            Assert.True(context.Room.Lyrics[61].IsSent);
        }
        finally
        {
            context.Room.StopAutoLyric();

            if (playback is not null)
            {
                await playback;
            }

            await CloseWindowAsync(window);
        }
    }

    private static Window CreateWindow(LyricNavigationViewTestContext context, bool useMainWindow)
    {
        if (useMainWindow)
        {
            return new MainWindow { DataContext = context.ViewModel };
        }

        return new()
        {
            Width = 1100, Height = 800, DataContext = context.ViewModel,
            Content = new LiveRoomWorkspaceView(new(context.ViewModel, context.Room)) { DataContext = context.Room }
        };
    }

    private static async Task<LyricsPanelView> ShowLyricsAsync(Window window)
    {
        window.Show();

        if (window is MainWindow mainWindow)
        {
            await mainWindow.Initialization;
            Assert.NotNull(mainWindow.Workbench);
            mainWindow.Workbench.Layouts.ShowPanel(WorkbenchPanelIds.LYRICS);
            PumpLayout(window);
            return mainWindow.Workbench.Panels.Lyrics;
        }

        var view = Assert.IsType<LiveRoomWorkspaceView>(window.Content);
        view.GetVisualDescendants().OfType<TabControl>()
            .Single(control => control.Classes.Contains("workspaceToolTabs")).SelectedIndex = 2;
        PumpLayout(window);
        return view.WorkbenchPanels!.Lyrics;
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static Point GetCenter(Control control, Window window)
    {
        return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    }

    private static void Click(Window window, Control control)
    {
        var point = GetCenter(control, window);
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        PumpLayout(window);
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        PumpLayout(window);
    }

    private static async Task CloseWindowAsync(Window window)
    {
        if (window is MainWindow mainWindow)
        {
            await mainWindow.CloseWorkbenchAsync();
        }
        else
        {
            window.Close();
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void SavePreview(Window window, double width)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_LYRIC_QA_OUTPUT");

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"navigation-{width}.png"));
    }
}
