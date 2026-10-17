using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证歌词操作栏在窄面板、主题与缩放下的可用性及发送交互。
/// </summary>
public class LyricsPanelToolbarTests
{
    /// <summary>
    /// 覆盖窄面板到宽面板、浅深主题与常用界面缩放。
    /// </summary>
    public static IEnumerable<object[]> LayoutCases
    {
        get
        {
            foreach (var width in new[] { 300d, 360d, 480d, 800d })
            {
                foreach (var dark in new[] { false, true })
                {
                    foreach (var scale in new[] { 1d, 1.5d, 2d })
                    {
                        yield return [width, dark, scale];
                    }
                }
            }
        }
    }

    /// <summary>
    /// 长标题不会挤出操作按钮，各组换行后仍完整可点击且不遮挡歌词。
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(LayoutCases))]
    public void ToolbarRemainsUsableAcrossWidthsThemesAndScales(double width, bool dark, double scale)
    {
        using var context = new LyricNavigationViewTestContext();
        context.Room.LyricTitle = "暖暖 · " + new string('长', 80);
        using var view = new LyricsPanelView(new(context.ViewModel, context.Room));
        var window = CreateWindow(view, width, dark, scale);

        try
        {
            window.Show();
            PumpLayout(window);
            var buttons = view.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name is { } name &&
                                 (name.StartsWith("Lyric", StringComparison.Ordinal) ||
                                  name.StartsWith("NextLyric", StringComparison.Ordinal)))
                .ToArray();
            Assert.NotEmpty(buttons);
            var controls = buttons.Cast<Control>().Concat(
            [
                view.FindControl<ComboBox>("LyricSendModeSelector")!,
                view.FindControl<CheckBox>("PreventRepeatedLyricSendCheckBox")!,
                view.FindControl<TextBox>("LyricPlaybackRateInput")!
            ]).ToArray();
            var listBounds = GetBounds(view.FindControl<ListBox>("LyricListBox")!, window);
            Assert.True(listBounds.Height > 100 * scale);

            var actions = view.FindControl<Grid>("LyricSendActions")!;
            var toolbar = view.FindControl<WrapPanel>("LyricPlaybackToolbar")!;
            var rateControls = view.FindControl<Grid>("LyricPlaybackRateControls")!;
            var separator = view.FindControl<Border>("LyricPlaybackRateSeparator")!;
            Assert.Same(toolbar, actions.Parent);
            Assert.Same(toolbar, rateControls.Parent);
            Assert.True(separator.Bounds.Width < separator.Bounds.Height);

            if (width >= 480)
            {
                Assert.Equal(actions.Bounds.Top, rateControls.Bounds.Top, 3);
                Assert.True(rateControls.Bounds.Left > actions.Bounds.Right);
            }
            else
            {
                Assert.True(rateControls.Bounds.Top >= actions.Bounds.Bottom);
            }

            var actionButtons = actions.Children.OfType<Button>().ToArray();

            foreach (var button in actionButtons.Skip(1))
            {
                Assert.Equal(button.Bounds.Width, button.Bounds.Height, 3);
                Assert.Equal(actionButtons[0].Bounds.Top, button.Bounds.Top, 3);
                Assert.Equal(actionButtons[0].Bounds.Height, button.Bounds.Height, 3);
                Assert.InRange(button.Bounds.Right, 0, actions.Bounds.Width + 0.5);
            }

            for (var index = 0; index < controls.Length; index++)
            {
                var control = controls[index];
                var bounds = GetBounds(control, window);
                Assert.True(control.IsEffectivelyVisible, control.Name);
                Assert.True(bounds.Width > 0 && bounds.Height > 0, control.Name);
                Assert.InRange(bounds.Left, 0, window.Bounds.Width);
                Assert.InRange(bounds.Right, 0, window.Bounds.Width + 0.5);
                Assert.True(bounds.Bottom <= listBounds.Top + 0.5, control.Name);
                var hit = window.InputHitTest(bounds.Center);
                Assert.True(ReferenceEquals(hit, control) ||
                            hit is Visual visual && visual.GetVisualAncestors().Contains(control), control.Name);

                foreach (var other in controls.Skip(index + 1))
                {
                    var otherBounds = GetBounds(other, window);
                    Assert.False(bounds.Intersects(otherBounds),
                        $"{control.Name} {bounds} 与 {other.Name} {otherBounds} 重叠");
                }
            }

            foreach (var group in view.GetVisualDescendants().OfType<WrapPanel>())
            {
                foreach (var child in group.Children)
                {
                    Assert.InRange(child.Bounds.Right, 0, group.Bounds.Width + 0.5);
                    Assert.InRange(child.Bounds.Bottom, 0, group.Bounds.Height + 0.5);
                }
            }

            Click(window, view.FindControl<Button>("NextLyricButton")!);
            Assert.Equal(["【♪第二句】"], context.SendService.SentMessages);
            context.Room.LyricTitle = "暖暖";
            SavePreview(window, $"toolbar-{width}-{(dark ? "dark" : "light")}-{scale}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 重排后微调、倍速、自动发送、暂停、停止及更多菜单清空仍保持原语义。
    /// </summary>
    [AvaloniaFact]
    public async Task ToolbarPreservesPlaybackControlsAndClearsThroughMoreMenu()
    {
        using var context = new LyricNavigationViewTestContext();
        using var view = new LyricsPanelView(new(context.ViewModel, context.Room));
        var window = CreateWindow(view, 300, true, 1);
        Task? playback = null;
        MenuFlyout? more = null;

        try
        {
            window.Show();
            PumpLayout(window);
            context.Room.SeekLyricLineStart(context.Room.Lyrics[1]);
            Click(window, view.FindControl<Button>("LyricRewindButton")!);
            Assert.Equal(9.5, context.Room.LyricPlaybackPositionSeconds, 3);
            Click(window, view.FindControl<Button>("LyricFastForwardButton")!);
            Assert.Equal(10, context.Room.LyricPlaybackPositionSeconds, 3);
            Click(window, view.FindControl<Button>("LyricRateIncreaseButton")!);
            Assert.Equal(1.1, context.Room.LyricPlaybackRate, 3);
            Click(window, view.FindControl<Button>("LyricRateDecreaseButton")!);
            Assert.Equal(1, context.Room.LyricPlaybackRate, 3);

            var auto = view.FindControl<Button>("LyricAutoSendButton")!;
            Click(window, auto);
            playback = Assert.IsAssignableFrom<IAsyncRelayCommand>(auto.Command).ExecutionTask;
            Assert.True(context.Room.IsLyricAutoSending);
            Assert.Equal("暂停发送", auto.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.IsVisible).Text);
            Click(window, auto);
            Assert.False(context.Room.IsLyricAutoSending);

            if (playback is not null)
            {
                await playback;
            }

            Assert.Equal("自动发送", auto.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.IsVisible).Text);
            Click(window, view.FindControl<Button>("LyricStopButton")!);
            Assert.Equal(1, context.Room.LyricPlaybackPositionSeconds, 3);

            var originalPreventRepeatedSend = context.Room.PreventRepeatedLyricSend;
            Click(window, view.FindControl<CheckBox>("PreventRepeatedLyricSendCheckBox")!);
            Assert.Equal(!originalPreventRepeatedSend, context.Room.PreventRepeatedLyricSend);

            var moreButton = view.FindControl<Button>("LyricMoreButton")!;
            Click(window, moreButton);
            more = Assert.IsType<MenuFlyout>(moreButton.Flyout);
            Assert.True(more.IsOpen);
            var clear = Assert.Single(more.Items.OfType<MenuItem>());
            Assert.Equal("清空歌词", clear.Header);
            Assert.Same(context.Room.ClearLyricCommand, clear.Command);
            Click(window, clear);
            Assert.Empty(context.Room.Lyrics);
            Assert.False(more.IsOpen);
        }
        finally
        {
            more?.Hide();
            context.Room.StopAutoLyric();

            if (playback is not null)
            {
                await playback;
            }

            window.Close();
        }
    }

    /// <summary>
    /// 相邻的下一句按钮分别保留暂停或启动播放，均只发送一次目标行。
    /// </summary>
    [AvaloniaFact]
    public async Task NextButtonsPreservePauseOrStartPlaybackAsRequested()
    {
        using var context = new LyricNavigationViewTestContext();
        using var view = new LyricsPanelView(new(context.ViewModel, context.Room));
        var window = CreateWindow(view, 300, true, 1);
        Task? playback = null;

        try
        {
            window.Show();
            PumpLayout(window);
            var next = view.FindControl<Button>("NextLyricButton")!;
            var nextAndPlay = view.FindControl<Button>("NextLyricAndPlayButton")!;
            Assert.Same(next.Parent, nextAndPlay.Parent);
            Assert.True(next.Bounds.Right < nextAndPlay.Bounds.Left);
            Click(window, next);
            Assert.False(context.Room.IsLyricAutoSending);
            Click(window, nextAndPlay);
            playback = Assert.IsAssignableFrom<IAsyncRelayCommand>(nextAndPlay.Command).ExecutionTask;

            Assert.True(context.Room.IsLyricAutoSending);
            Assert.Same(context.Room.Lyrics[2], context.Room.ActiveLyricLine);
            Assert.Equal(["【♪第二句】", "【♪第三句】"], context.SendService.SentMessages);
            Assert.False(next.IsEffectivelyEnabled);
            Assert.False(nextAndPlay.IsEffectivelyEnabled);
        }
        finally
        {
            context.Room.StopAutoLyric();

            if (playback is not null)
            {
                await playback;
            }

            window.Close();
        }
    }

    private static Window CreateWindow(Control content, double width, bool dark, double scale)
    {
        return new()
        {
            Width = width * scale,
            Height = 360 * scale,
            SystemDecorations = SystemDecorations.None,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(scale, scale),
                Child = content
            }
        };
    }

    private static Rect GetBounds(Control control, Visual relativeTo)
    {
        return new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(relativeTo)!.Value);
    }

    private static void Click(Window window, Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = GetBounds(control, root).Center;
        root.MouseMove(point);
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        PumpLayout(window);
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void SavePreview(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_LYRIC_QA_OUTPUT");

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        window.MouseMove(new(-1, -1));
        PumpLayout(window);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"{name}.png"));
    }
}