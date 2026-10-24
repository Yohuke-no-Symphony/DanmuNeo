using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证独立输入面板在拉高、收窄及显示错误时的真实编辑区域和操作区布局。
/// </summary>
public class WorkbenchComposerLayoutTests
{
    /// <summary>
    /// 异步加载配置后，颜色和方向角标在不同缩放、主题及悬停状态下完整显示。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 1.25)]
    [InlineData(true, 1.25)]
    [InlineData(false, 1.5)]
    [InlineData(true, 1.5)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task SelectorBadgesUpdateAfterAsynchronousConfigurationLoad(bool dark, double scale)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        var configuration = fixture.DanmuConfigService.Config with
        {
            Current = new() { Color = 0xFF0000, Mode = 5 }
        };
        var loaded = new TaskCompletionSource<DanmuRoomConfig>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DanmuConfigService.ReadAsync = (_, _, token, _) => loaded.Task.WaitAsync(token);
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var composer = new DraftComposerPanelView(context);
        var window = new Window
        {
            Width = 360 * scale, Height = 160 * scale,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light,
            Content = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(scale, scale), Child = composer
            }
        };

        try
        {
            window.Show();
            Pump(window);
            Assert.True(fixture.Room.IsLoadingDanmuConfiguration);
            loaded.SetResult(configuration);
            await fixture.Room.DanmuConfigurationTask;
            Pump(window);
            var color = composer.FindControl<ComboBox>("DanmuColorSelector")!;
            var mode = composer.FindControl<ComboBox>("DanmuModeSelector")!;
            Assert.Equal(0xFF0000, Assert.IsType<DanmuColorOption>(color.SelectedItem).Value);
            Assert.Equal(5, Assert.IsType<DanmuModeOption>(mode.SelectedItem).Value);

            foreach (var hovered in new[] { false, true })
            {
                foreach (var selector in new[] { color, mode })
                {
                    var point = hovered
                        ? selector.TranslatePoint(new Point(selector.Bounds.Width / 2, selector.Bounds.Height / 2), window)!.Value
                        : new Point(-1, -1);
                    window.MouseMove(point);
                    Pump(window);
                    var badge = composer.FindControl<Border>(selector == color ? "ComposerColorBadge" : "ComposerModeBadge")!;
                    Assert.True(badge.IsEffectivelyVisible);
                    Assert.True(badge.Bounds.Width > 0 && badge.Bounds.Height > 0);
                    var badgeBounds = new Rect(badge.Bounds.Size).TransformToAABB(badge.TransformToVisual(selector)!.Value);
                    Assert.True(new Rect(selector.Bounds.Size).Contains(badgeBounds));

                    if (hovered)
                    {
                        var hit = window.InputHitTest(badge.TranslatePoint(
                            new Point(badge.Bounds.Width / 2, badge.Bounds.Height / 2), window)!.Value);
                        Assert.True(ReferenceEquals(hit, selector) ||
                                    hit is Visual visual && visual.GetVisualAncestors().Contains(selector));
                    }

                    foreach (var clip in badge.GetVisualAncestors().OfType<Control>().Where(control => control.ClipToBounds))
                    {
                        var bounds = new Rect(badge.Bounds.Size).TransformToAABB(badge.TransformToVisual(clip)!.Value);
                        Assert.True(new Rect(clip.Bounds.Size).Inflate(0.5).Contains(bounds),
                            $"{badge.Name} 被 {clip.GetType().Name}#{clip.Name} 裁剪：{bounds} / {clip.Bounds.Size}");
                    }
                }

                var colorBadge = composer.FindControl<Border>("ComposerColorBadge")!;
                Assert.Equal(Colors.Red, Assert.IsAssignableFrom<ISolidColorBrush>(colorBadge.Background).Color);
                var modeBadge = composer.FindControl<LucideIcon>("ComposerModeBadgeIcon")!;
                Assert.Equal(LucideIconKind.ArrowUpToLine, modeBadge.Kind);
                SaveFrame(window, $"composer-badges-{dark}-{scale}-{hovered}.png");
            }

            Assert.Empty(fixture.DanmuConfigService.SetRequests);
        }
        finally
        {
            loaded.TrySetCanceled();
            window.Close();
        }
    }

    /// <summary>
    /// 错误信息不挤散操作区，发送按钮始终靠右，输入内容从顶部开始并随高度伸展。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(760, 180)]
    [InlineData(760, 420)]
    [InlineData(360, 320)]
    [InlineData(240, 400)]
    public void ComposerUsesAvailableHeightAndKeepsActionsAtRight(double width, double height)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var composer = new DraftComposerPanelView(context);
        var window = new Window { Width = width, Height = height, Content = composer };

        try
        {
            window.Show();
            Pump(window);
            var input = composer.Input;
            var border = composer.GetVisualDescendants().OfType<Border>()
                .Single(control => control.Classes.Contains("translateComposer"));
            var send = composer.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("translateComposerSendButton"));
            var sendRight = send.TranslatePoint(new Point(send.Bounds.Width, 0), border)!.Value.X;
            Assert.InRange(border.Bounds.Width - border.Padding.Right - border.BorderThickness.Right - sendRight, 0,
                0.5);
            Assert.True(input.Bounds.Height >= input.MinHeight);
            var previousInputHeight = input.Bounds.Height;
            window.Height += 80;
            Pump(window);
            Assert.InRange(input.Bounds.Height - previousInputHeight, 79.5, 80.5);
            input.Text = "测试同传";
            Pump(window);
            var presenter = input.GetVisualDescendants().OfType<TextPresenter>().Single();
            Assert.InRange(presenter.TranslatePoint(default, input)!.Value.Y, 0, 1);
            SaveFrame(window, $"composer-{width}x{height}-normal.png");

            var before = send.TranslatePoint(default, composer)!.Value;
            fixture.Room.DraftSendError = "发送失败：" + new string('错', 100);
            fixture.Room.IsDraftSendFailed = true;
            Pump(window);
            Assert.Equal(before, send.TranslatePoint(default, composer)!.Value);
            var error = composer.FindControl<TextBlock>("DraftSendErrorText")!;
            var errorBottom = error.TranslatePoint(new Point(0, error.Bounds.Height), composer)!.Value.Y;
            var actions = new Control[]
            {
                composer.FindControl<ComboBox>("DanmuColorSelector")!,
                composer.FindControl<ComboBox>("DanmuModeSelector")!,
                send
            };

            foreach (var action in actions)
            {
                var origin = action.TranslatePoint(default, composer)!.Value;
                Assert.True(origin.Y >= errorBottom + 3.5);
                Assert.InRange(origin.X, 0, composer.Bounds.Width);
                Assert.InRange(origin.X + action.Bounds.Width, 0, composer.Bounds.Width);
            }

            SaveFrame(window, $"composer-{width}x{height}-error.png");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 未分配固定高度时输入面板仍保留可编辑的最小区域。
    /// </summary>
    [AvaloniaFact]
    public void ComposerHasUsefulIntrinsicMinimumHeight()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var composer = new DraftComposerPanelView(context);
        var window = new Window { Width = 760, SizeToContent = SizeToContent.Height, Content = composer };

        try
        {
            window.Show();
            Pump(window);
            Assert.InRange(composer.Input.Bounds.Height, 20, 24);
            Assert.InRange(composer.Bounds.Height, 76, 88);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>同一面板在宽窄切换时始终显示方形图标，并保持草稿、焦点和选择。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComposerAlwaysUsesSquareSelectorsWithoutChangingDraftOrConfiguration(bool dark)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var composer = new DraftComposerPanelView(context);
        var window = new Window
        {
            Width = 360, Height = 200, Content = composer,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light
        };

        try
        {
            window.Show();
            await fixture.Room.DanmuConfigurationTask;
            Pump(window);
            var color = composer.FindControl<ComboBox>("DanmuColorSelector")!;
            var mode = composer.FindControl<ComboBox>("DanmuModeSelector")!;
            var selectedColor = color.SelectedItem;
            var selectedMode = mode.SelectedItem;
            composer.Input.Text = "保留中文草稿和选区";
            Assert.True(composer.Input.Focus());
            composer.Input.SelectionStart = 2;
            composer.Input.SelectionEnd = 5;

            foreach (var width in new[] { 760, 360, 240, 760 })
            {
                window.Width = width;
                Pump(window);
                Assert.InRange(Math.Abs(color.Bounds.Width - color.Bounds.Height), 0, 0.5);
                Assert.InRange(Math.Abs(mode.Bounds.Width - mode.Bounds.Height), 0, 0.5);
                Assert.Equal(34, color.Bounds.Width);
                Assert.Equal(34, mode.Bounds.Width);
                Assert.Equal(color.TranslatePoint(default, composer)!.Value.Y, mode.TranslatePoint(default, composer)!.Value.Y);

                Assert.Same(selectedColor, color.SelectedItem);
                Assert.Same(selectedMode, mode.SelectedItem);
                Assert.True(composer.Input.IsFocused);
                Assert.Equal("保留中文草稿和选区", composer.Input.Text);
                Assert.Equal(2, composer.Input.SelectionStart);
                Assert.Equal(5, composer.Input.SelectionEnd);
                Assert.Single(fixture.DanmuConfigService.GetRequests);
                Assert.Empty(fixture.DanmuConfigService.SetRequests);
                SaveFrame(window, $"composer-responsive-{width}-{(dark ? "dark" : "light")}.png");
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>紧凑图标按真实鼠标路径展开正常宽度的选项，选中后保存并更新颜色角标。</summary>
    [AvaloniaFact]
    public async Task CompactSelectorsOpenAndSaveOptionsThroughPointerInput()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var composer = new DraftComposerPanelView(context);
        var window = new Window { Width = 240, Height = 200, Content = composer };

        try
        {
            window.Show();
            await fixture.Room.DanmuConfigurationTask;
            Pump(window);
            var color = composer.FindControl<ComboBox>("DanmuColorSelector")!;
            var mode = composer.FindControl<ComboBox>("DanmuModeSelector")!;
            Click(color);
            Assert.True(color.IsDropDownOpen);
            var red = Assert.IsType<ComboBoxItem>(color.ContainerFromIndex(1));
            Assert.True(red.Bounds.Width > color.Bounds.Width);
            Click(red);
            await fixture.Room.DanmuConfigurationTask;
            Pump(window);
            Assert.False(color.IsDropDownOpen);
            var badge = composer.FindControl<Border>("ComposerColorBadge")!;
            Assert.Equal(Colors.Red, Assert.IsAssignableFrom<ISolidColorBrush>(badge.Background).Color);

            Click(mode);
            Assert.True(mode.IsDropDownOpen);
            Click(Assert.IsType<ComboBoxItem>(mode.ContainerFromIndex(2)));
            await fixture.Room.DanmuConfigurationTask;
            Pump(window);
            Assert.False(mode.IsDropDownOpen);
            var modeBadge = composer.FindControl<LucideIcon>("ComposerModeBadgeIcon")!;
            Assert.Equal(LucideIconKind.ArrowUpToLine, modeBadge.Kind);
            Assert.Equal(2, fixture.DanmuConfigService.SetRequests.Count);
            Assert.Equal(new() { Color = 0xFF0000, Mode = 5 }, fixture.DanmuConfigService.SetRequests.Last().Options);
            SaveFrame(window, "composer-compact-selected-options.png");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>读取失败时紧凑按钮仍显示基础图标，点击可重试读取且不产生保存。</summary>
    [AvaloniaFact]
    public async Task CompactSelectorWithNoSelectionKeepsIconAndCanRetryLoading()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.DanmuConfigService.Exception = new IOException("读取失败");
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var composer = new DraftComposerPanelView(context);
        var window = new Window { Width = 240, Height = 160, Content = composer };

        try
        {
            window.Show();
            await fixture.Room.DanmuConfigurationTask;
            Pump(window);
            var color = composer.FindControl<ComboBox>("DanmuColorSelector")!;
            Assert.True(color.IsEnabled);
            Assert.Null(color.SelectedItem);
            Assert.True(color.SelectionBoxItemTemplate?.Match(null), "紧凑模板必须能呈现无选中项的基础图标");
            Assert.Contains(color.GetVisualDescendants().OfType<LucideIcon>(), icon => icon.Kind == LucideIconKind.Palette && icon.IsEffectivelyVisible);
            Assert.False(composer.FindControl<Border>("ComposerColorBadge")!.IsVisible);
            SaveFrame(window, "composer-compact-empty-selection.png");

            fixture.DanmuConfigService.Exception = null;
            Click(color);
            await fixture.Room.DanmuConfigurationTask;
            Pump(window);
            Assert.True(color.IsDropDownOpen);
            Assert.NotNull(color.SelectedItem);
            Assert.Equal(2, fixture.DanmuConfigService.GetRequests.Count);
            Assert.Empty(fixture.DanmuConfigService.SetRequests);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>最小一行编辑区域内的长句仍可换行并滚动到光标末尾。</summary>
    [AvaloniaTheory]
    [InlineData(760)]
    [InlineData(240)]
    public void OneLineMinimumKeepsLongDraftScrollable(double width)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var composer = new DraftComposerPanelView(context);
        var window = new Window { Width = width, SizeToContent = SizeToContent.Height, Content = composer };

        try
        {
            window.Show();
            Pump(window);
            window.SizeToContent = SizeToContent.Manual;
            window.Height = composer.MinHeight;
            composer.Input.Text = string.Concat(Enumerable.Repeat("一行高度下仍然完整保留并能滚动的中文长句", 20));
            Assert.True(composer.Input.Focus());
            composer.Input.CaretIndex = composer.Input.Text.Length;
            Pump(window);
            var scroll = composer.Input.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.InRange(composer.Input.Bounds.Height, 20, 24);
            Assert.True(scroll.Viewport.Height > 0);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(scroll.Offset.Y > 0);
            Assert.Equal(composer.Input.Text.Length, composer.Input.CaretIndex);
            SaveFrame(window, $"composer-{width}-one-line-scrolled-draft.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static void Click(Control control)
    {
        var owner = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control));
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), owner)!.Value;
        owner.MouseDown(point, MouseButton.Left);
        owner.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        owner.UpdateLayout();
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void SaveFrame(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_DOCK_QA_OUTPUT");

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, name));
        }
    }
}
