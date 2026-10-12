using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Views.Components;

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
    /// 平台复制手势优先复制手动选中行的翻译，缺少翻译时复制原文。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control, RawInputModifiers.Control, "选中句的翻译", "选中句的翻译")]
    [InlineData(KeyModifiers.Meta, RawInputModifiers.Meta, "选中句的翻译", "选中句的翻译")]
    [InlineData(KeyModifiers.Control, RawInputModifiers.Control, "", "第二句原文")]
    [InlineData(KeyModifiers.Meta, RawInputModifiers.Meta, "", "第二句原文")]
    [InlineData(KeyModifiers.Control, RawInputModifiers.Control, " \t", "第二句原文")]
    [InlineData(KeyModifiers.Meta, RawInputModifiers.Meta, " \t", "第二句原文")]
    public async Task CopyGestureUsesSelectedLineInsteadOfPlayingLine(KeyModifiers modifiers,
        RawInputModifiers rawModifiers, string translation, string expected)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context);
        context.Room.Lyrics[1].TranslatedContent = translation;
        var window = CreateWindow(context, view);
        var hotkeys = window.PlatformSettings!.HotkeyConfiguration;
        var originalCopy = hotkeys.Copy;
        hotkeys.Copy = [new(Key.C, modifiers)];

        try
        {
            ShowWindow(window, view);
            var list = view.FindControl<ListBox>("LyricListBox")!;
            var playingLine = context.Room.ActiveLyricLine;
            Click(window, list.ContainerFromIndex(1)!);
            Assert.Same(context.Room.Lyrics[1], list.SelectedItem);
            Assert.NotSame(playingLine, list.SelectedItem);
            await window.Clipboard!.SetTextAsync("原剪贴板");
            PressCopy(window, PhysicalKey.C, rawModifiers);

            Assert.Equal(expected, await window.Clipboard.TryGetTextAsync());
            Assert.Same(playingLine, context.Room.ActiveLyricLine);
            Assert.False(context.Room.IsLyricAutoSending);
        }
        finally
        {
            hotkeys.Copy = originalCopy;
            window.Close();
        }
    }

    /// <summary>
    /// 使用平台定义的备用复制手势，未选中歌词时保留剪贴板。
    /// </summary>
    [AvaloniaFact]
    public async Task AlternatePlatformCopyGestureAndEmptySelectionWork()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context);
        var window = CreateWindow(context, view);
        var hotkeys = window.PlatformSettings!.HotkeyConfiguration;
        var originalCopy = hotkeys.Copy;
        hotkeys.Copy = [new(Key.Insert, KeyModifiers.Control)];

        try
        {
            ShowWindow(window, view);
            var list = view.FindControl<ListBox>("LyricListBox")!;
            Click(window, list.ContainerFromIndex(1)!);
            PressCopy(window, PhysicalKey.Insert, RawInputModifiers.Control);
            Assert.Equal("第二句翻译", await window.Clipboard!.TryGetTextAsync());

            context.Room.ClearLyric();
            PumpLayout(window);
            Assert.Null(list.SelectedItem);
            await window.Clipboard!.SetTextAsync("空列表前的剪贴板");
            PressCopy(window, PhysicalKey.Insert, RawInputModifiers.Control);
            Assert.Equal("空列表前的剪贴板", await window.Clipboard.TryGetTextAsync());
        }
        finally
        {
            hotkeys.Copy = originalCopy;
            window.Close();
        }
    }

    /// <summary>
    /// 歌词有选中行时，输入框中的复制仍复制输入框选中的文字。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(KeyModifiers.Control, RawInputModifiers.Control)]
    [InlineData(KeyModifiers.Meta, RawInputModifiers.Meta)]
    public async Task CopyInDraftKeepsTextBoxSelectionBehavior(KeyModifiers modifiers, RawInputModifiers rawModifiers)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = CreateView(context);
        var window = CreateWindow(context, view);
        var hotkeys = window.PlatformSettings!.HotkeyConfiguration;
        var originalCopy = hotkeys.Copy;
        hotkeys.Copy = [new(Key.C, modifiers)];

        try
        {
            ShowWindow(window, view);
            var list = view.FindControl<ListBox>("LyricListBox")!;
            Click(window, list.ContainerFromIndex(1)!);
            var input = view.FindControl<TextBox>("InputDraftTextBox")!;
            input.Text = "输入框中的文字";
            Assert.True(input.Focus());
            input.SelectionStart = 0;
            input.SelectionEnd = 3;
            PressCopy(window, PhysicalKey.C, rawModifiers);

            Assert.Equal("输入框", await window.Clipboard!.TryGetTextAsync());
            Assert.Equal("输入框中的文字", input.Text);
            Assert.Same(context.Room.Lyrics[1], list.SelectedItem);
        }
        finally
        {
            hotkeys.Copy = originalCopy;
            window.Close();
        }
    }

    private static LiveRoomWorkspaceView CreateView(LiveRoomFeedViewTestContext context)
    {
        context.Room.LyricInput = "[00:01]第一句原文\n[00:02]第二句原文\n[00:03]第三句原文";
        context.Room.TranslatedLyricInput = "[00:01]第一句翻译\n[00:02]第二句翻译\n[00:03]第三句翻译";
        context.Room.ApplyLyric();
        return new() { DataContext = context.Room };
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
