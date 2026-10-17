using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 歌词标题点击复制和选中行复制的真实界面回归测试。
/// </summary>
public class LyricCopyViewTests
{
    /// <summary>
    /// 标题从无歌词到加载、切换和清空时，复制完整标题或保持原剪贴板。
    /// </summary>
    [AvaloniaFact]
    public async Task TitleClickCopiesFullTitleAndTracksLoadingAndClearing()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context);
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window, view);
            var clipboard = window.Clipboard!;
            var title = Assert.IsType<Button>(view.FindControl<Button>("LyricTitleButton"));
            await clipboard.SetTextAsync("原剪贴板");
            Assert.False(title.IsEnabled);
            Click(window, title);
            Assert.Equal("原剪贴板", await clipboard.TryGetTextAsync());

            var fullTitle = new string('长', 120) + "完整歌曲标题";
            context.Room.LyricTitle = fullTitle;
            PumpLayout(window);
            Assert.True(title.IsEnabled);
            Assert.Equal(fullTitle, title.GetVisualDescendants().OfType<TextBlock>().Single().Text);
            Click(window, title);
            Assert.Equal(fullTitle, await clipboard.TryGetTextAsync());

            context.Room.LyricTitle = "另一首歌曲";
            PumpLayout(window);
            Click(window, title);
            Assert.Equal("另一首歌曲", await clipboard.TryGetTextAsync());
            context.Room.ClearLyric();
            PumpLayout(window);
            Assert.False(title.IsEnabled);
            Click(window, title);
            Assert.Equal("另一首歌曲", await clipboard.TryGetTextAsync());
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 可配置复制手势按发送模式复制手动选中的歌词行，译文缺失时使用原文。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control, RawInputModifiers.Control, LyricSendMode.TranslationOnly,
        "选中句的翻译", "选中句的翻译")]
    [InlineData(KeyModifiers.Meta, RawInputModifiers.Meta, LyricSendMode.TranslationOnly,
        "选中句的翻译", "选中句的翻译")]
    [InlineData(KeyModifiers.Control, RawInputModifiers.Control, LyricSendMode.OriginalOnly,
        "选中句的翻译", "第二句原文")]
    [InlineData(KeyModifiers.Meta, RawInputModifiers.Meta, LyricSendMode.OriginalOnly,
        "选中句的翻译", "第二句原文")]
    [InlineData(KeyModifiers.Control, RawInputModifiers.Control, LyricSendMode.Bilingual,
        "选中句的翻译", "第二句原文 / 选中句的翻译")]
    [InlineData(KeyModifiers.Meta, RawInputModifiers.Meta, LyricSendMode.Bilingual,
        "选中句的翻译", "第二句原文 / 选中句的翻译")]
    [InlineData(KeyModifiers.Control, RawInputModifiers.Control, LyricSendMode.TranslationOnly, "", "第二句原文")]
    [InlineData(KeyModifiers.Meta, RawInputModifiers.Meta, LyricSendMode.TranslationOnly, "", "第二句原文")]
    [InlineData(KeyModifiers.Control, RawInputModifiers.Control, LyricSendMode.Bilingual, "", "第二句原文")]
    [InlineData(KeyModifiers.Meta, RawInputModifiers.Meta, LyricSendMode.Bilingual, "", "第二句原文")]
    [InlineData(KeyModifiers.Control, RawInputModifiers.Control, LyricSendMode.TranslationOnly, " \t", "第二句原文")]
    [InlineData(KeyModifiers.Meta, RawInputModifiers.Meta, LyricSendMode.Bilingual, " \t", "第二句原文")]
    public async Task CopyGestureUsesSelectedLineInsteadOfPlayingLine(KeyModifiers modifiers,
        RawInputModifiers rawModifiers, LyricSendMode mode, string translation, string expected)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context);
        context.Room.LyricSendMode = mode;
        context.Room.Lyrics[1].TranslatedContent = translation;
        var window = CreateWindow(context, view);
        SetCopyGesture(context, Key.V, modifiers);

        try
        {
            ShowWindow(window, view);
            var list = view.FindControl<ListBox>("LyricListBox")!;
            var playingLine = context.Room.ActiveLyricLine;
            Click(window, list.ContainerFromIndex(1)!);
            Assert.Same(context.Room.Lyrics[1], list.SelectedItem);
            Assert.NotSame(playingLine, list.SelectedItem);
            await window.Clipboard!.SetTextAsync("原剪贴板");
            PressCopy(window, PhysicalKey.V, rawModifiers);

            Assert.Equal(expected, await window.Clipboard.TryGetTextAsync());
            Assert.Same(playingLine, context.Room.ActiveLyricLine);
            Assert.False(context.Room.IsLyricAutoSending);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 改绑后使用自定义手势，默认手势和空列表均保持剪贴板。
    /// </summary>
    [AvaloniaFact]
    public async Task CustomCopyGestureReplacesDefaultAndPreservesClipboardWithoutSelection()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context);
        var window = CreateWindow(context, view);
        SetCopyGesture(context, Key.Insert, KeyModifiers.Control);

        try
        {
            ShowWindow(window, view);
            var list = view.FindControl<ListBox>("LyricListBox")!;
            Click(window, list.ContainerFromIndex(1)!);
            await window.Clipboard!.SetTextAsync("原剪贴板");
            PressCopy(window, PhysicalKey.V, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
            Assert.Equal("原剪贴板", await window.Clipboard.TryGetTextAsync());
            PressCopy(window, PhysicalKey.Insert, RawInputModifiers.Control);
            Assert.Equal("第二句原文 / 第二句翻译", await window.Clipboard!.TryGetTextAsync());

            context.Room.ClearLyric();
            PumpLayout(window);
            Assert.Null(list.SelectedItem);
            await window.Clipboard!.SetTextAsync("空列表前的剪贴板");
            PressCopy(window, PhysicalKey.Insert, RawInputModifiers.Control);
            Assert.Equal("空列表前的剪贴板", await window.Clipboard.TryGetTextAsync());
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 歌词有选中行时，输入框保留自身的复制和粘贴行为。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control, RawInputModifiers.Control)]
    [InlineData(KeyModifiers.Meta, RawInputModifiers.Meta)]
    public async Task CopyAndPasteInDraftKeepTextBoxSelectionBehavior(KeyModifiers modifiers, RawInputModifiers rawModifiers)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context);
        var window = CreateWindow(context, view);
        var hotkeys = window.PlatformSettings!.HotkeyConfiguration;
        var originalCopy = hotkeys.Copy;
        hotkeys.Copy = [new(Key.C, modifiers)];
        var originalPaste = hotkeys.Paste;
        hotkeys.Paste = [new(Key.V, modifiers)];
        SetCopyGesture(context, Key.V, modifiers);

        try
        {
            ShowWindow(window, view);
            var clipboard = window.Clipboard!;
            var list = view.FindControl<ListBox>("LyricListBox")!;
            Click(window, list.ContainerFromIndex(1)!);
            var input = view.FindControl<TextBox>("InputDraftTextBox")!;
            input.Text = "输入框中的文字";
            Assert.True(input.Focus());
            input.SelectionStart = 0;
            input.SelectionEnd = 3;
            PressCopy(window, PhysicalKey.C, rawModifiers);

            Assert.Equal("输入框", await clipboard.TryGetTextAsync());
            Assert.Equal("输入框中的文字", input.Text);
            Assert.Same(context.Room.Lyrics[1], list.SelectedItem);

            await clipboard.SetTextAsync("粘贴文字");
            PressCopy(window, PhysicalKey.V, rawModifiers);
            Assert.Equal("粘贴文字中的文字", input.Text);
            Assert.Equal("粘贴文字", await clipboard.TryGetTextAsync());
        }
        finally
        {
            hotkeys.Copy = originalCopy;
            hotkeys.Paste = originalPaste;
            window.Close();
        }
    }

    /// <summary>
    /// 切换发送内容下拉框后，复制立即使用新模式；仅原文缺失时保留剪贴板。
    /// </summary>
    [AvaloniaFact]
    public async Task CopyTracksSendModeSelectionImmediately()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context);
        var window = CreateWindow(context, view);
        SetCopyGesture(context, Key.V, KeyModifiers.Control);

        try
        {
            ShowWindow(window, view);
            var list = view.FindControl<ListBox>("LyricListBox")!;
            Click(window, list.ContainerFromIndex(1)!);
            var modeSelector = view.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => ReferenceEquals(control.ItemsSource, context.Room.LyricSendModeOptions));

            foreach (var mode in new[] { LyricSendMode.TranslationOnly, LyricSendMode.OriginalOnly, LyricSendMode.Bilingual })
            {
                modeSelector.SelectedValue = mode;
                PumpLayout(window);
                Assert.Equal(mode, context.Room.LyricSendMode);
                Assert.True(list.ContainerFromIndex(1)!.Focus());
                PressCopy(window, PhysicalKey.V, RawInputModifiers.Control);
                var expected = mode switch
                {
                    LyricSendMode.TranslationOnly => "第二句翻译",
                    LyricSendMode.OriginalOnly => "第二句原文",
                    _ => "第二句原文 / 第二句翻译"
                };
                Assert.Equal(expected, await window.Clipboard!.TryGetTextAsync());
            }

            modeSelector.SelectedValue = LyricSendMode.OriginalOnly;
            context.Room.Lyrics[1].Content = " \t";
            await window.Clipboard!.SetTextAsync("原剪贴板");
            PressCopy(window, PhysicalKey.V, RawInputModifiers.Control);
            Assert.Equal("原剪贴板", await window.Clipboard.TryGetTextAsync());
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 禁用、清空、冲突、设置和录制期间，选中行不响应复制绑定。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("disabled")]
    [InlineData("cleared")]
    [InlineData("conflict")]
    [InlineData("settings")]
    [InlineData("recording")]
    public async Task CopyBindingHonorsDisabledConflictAndSettingsGuards(string mode)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context);
        var window = CreateWindow(context, view);
        SetCopyGesture(context, Key.V, KeyModifiers.Control);
        var binding = context.ViewModel.Settings.ShortcutBindings
            .Single(item => item.ActionKey == ShortcutActionKeys.LYRIC_COPY_SELECTED);

        try
        {
            ShowWindow(window, view);
            var list = view.FindControl<ListBox>("LyricListBox")!;
            Click(window, list.ContainerFromIndex(1)!);
            await window.Clipboard!.SetTextAsync("原剪贴板");

            switch (mode)
            {
                case "disabled":
                    binding.IsEnabled = false;
                    break;
                case "cleared":
                    binding.IsEnabled = false;
                    binding.GestureText = "";
                    break;
                case "conflict":
                    context.ViewModel.Settings.ShortcutBindings
                        .Single(item => item.ActionKey == ShortcutActionKeys.LYRIC_SEEK_NEXT).GestureText = "Ctrl+V";
                    break;
                case "settings":
                    context.ViewModel.IsSettingsOpen = true;
                    break;
                case "recording":
                    context.ViewModel.BeginRecordShortcut(context.ViewModel.ShortcutBindingGroups
                        .SelectMany(group => group.Bindings).Single(item => item.ActionKey == binding.ActionKey));
                    break;
            }

            PressCopy(window, PhysicalKey.V, RawInputModifiers.Control);

            Assert.Equal("原剪贴板", await window.Clipboard.TryGetTextAsync());
        }
        finally
        {
            context.ViewModel.CancelShortcutRecording();
            window.Close();
        }
    }

    /// <summary>
    /// 主窗和歌词浮窗中的默认手势只在歌词列表有焦点时复制，并在设置中显示绑定。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultCopyGestureRequiresListFocusInMainAndFloatingWindows(bool floatingHost)
    {
        using var context = new LiveRoomFeedViewTestContext();
        context.Room.LyricInput = "[00:01]第一句原文\n[00:02]第二句原文";
        context.Room.LyricSendMode = LyricSendMode.TranslationOnly;
        context.Room.ApplyLyric();
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.ShowPanel(WorkbenchPanelIds.LYRICS);
            Window host = main;

            if (floatingHost)
            {
                workbench.Layouts.FloatPanel(WorkbenchPanelIds.LYRICS);
                Dispatcher.UIThread.RunJobs();
                host = Assert.Single(workbench.Layouts.FloatingWindows);
            }

            host.Activate();
            PumpLayout(host);
            var view = workbench.Panels.Lyrics;
            var list = view.FindControl<ListBox>("LyricListBox")!;
            list.SelectedIndex = 1;
            Assert.True(list.ContainerFromIndex(1)!.Focus());
            var modifiers = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            PressCopy(host, PhysicalKey.V, modifiers);

            Assert.Equal("第二句原文", await host.Clipboard!.TryGetTextAsync());
            Assert.Same(context.Room.Lyrics[0], context.Room.ActiveLyricLine);
            var group = Assert.Single(context.ViewModel.ShortcutBindingGroups, item => item.Name == "歌词");
            var binding = Assert.Single(group.Bindings, item => item.ActionKey == ShortcutActionKeys.LYRIC_COPY_SELECTED);
            Assert.Equal("复制歌词", binding.Name);

            await host.Clipboard!.SetTextAsync("列表失焦后的剪贴板");
            Assert.True(view.FindControl<Button>("LyricMoreButton")!.Focus());
            PressCopy(host, PhysicalKey.V, modifiers);
            Assert.Equal("列表失焦后的剪贴板", await host.Clipboard.TryGetTextAsync());
        }
        finally
        {
            await main.CloseWorkbenchAsync();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void SetCopyGesture(LiveRoomFeedViewTestContext context, Key key, KeyModifiers modifiers)
    {
        Assert.True(ShortcutBindingService.TryCreateGestureText(key, modifiers, out var gesture));
        context.ViewModel.Settings.ShortcutBindings
            .Single(binding => binding.ActionKey == ShortcutActionKeys.LYRIC_COPY_SELECTED).GestureText = gesture;
    }

    private static LiveRoomWorkspaceView CreateView(LiveRoomFeedViewTestContext context)
    {
        context.Room.LyricInput = "[00:01]第一句原文\n[00:02]第二句原文\n[00:03]第三句原文";
        context.Room.TranslatedLyricInput = "[00:01]第一句翻译\n[00:02]第二句翻译\n[00:03]第三句翻译";
        context.Room.ApplyLyric();
        return new(new(context.ViewModel, context.Room)) { DataContext = context.Room };
    }

    private static Window CreateWindow(LiveRoomFeedViewTestContext context, LiveRoomWorkspaceView view)
    {
        return new() { Width = 1100, Height = 800, DataContext = context.ViewModel, Content = view };
    }

    private static void ShowWindow(Window window, LiveRoomWorkspaceView view)
    {
        window.Show();
        view.GetVisualDescendants().OfType<TabControl>()
            .Single(control => control.Classes.Contains("workspaceToolTabs")).SelectedIndex = 2;
        PumpLayout(window);
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        PumpLayout(window);
    }

    private static void PressCopy(Window window, PhysicalKey key, RawInputModifiers modifiers)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        PumpLayout(window);
    }
}
