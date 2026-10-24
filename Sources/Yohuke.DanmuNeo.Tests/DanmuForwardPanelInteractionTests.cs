using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 转发面板的多选、剪贴板、指针排序与紧凑布局验收。
/// </summary>
public class DanmuForwardPanelInteractionTests
{
    /// <summary>
    /// 启用和停用图标在普通、悬停及按下状态中始终与实际绘制背景保持可辨对比。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnabledIconKeepsContrastAcrossPointerStates(bool dark)
    {
        using var context = new DanmuForwardViewTestContext();
        using var selection = new WorkbenchContext(context.ViewModel, context.Room);
        using var view = new ForwardingPanelView(selection);
        var window = CreateWindow(view);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        try
        {
            window.Show();
            Pump(window);
            var rule = context.Room.ForwardRules[0];
            var button = Find<ToggleButton>(view, "ForwardEnabledToggle", rule);
            var icon = Assert.Single(button.GetVisualDescendants().OfType<LucideIcon>());
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
                .Single(control => control.Name == "PART_ContentPresenter");
            var card = Find<Border>(view, "ForwardRuleCard", rule);
            var surface = Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color;

            foreach (var enabled in new[] { true, false })
            {
                button.IsChecked = enabled;
                window.MouseMove(new(-1, -1));
                Pump(window);
                AssertIconContrast(icon, presenter, surface);
                SaveIconFrame(window, $"forward-toggle-{dark}-{enabled}-normal.png");

                var center = Center(button, window);
                window.MouseMove(center);
                Pump(window);
                AssertIconContrast(icon, presenter, surface);
                SaveIconFrame(window, $"forward-toggle-{dark}-{enabled}-hover.png");

                window.MouseDown(center, MouseButton.Left);
                Pump(window);
                AssertIconContrast(icon, presenter, surface);
                window.MouseUp(center, MouseButton.Left);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 标题按列表顺序复制选中项或全量，条目复制只复制当前项且保留选择。
    /// </summary>
    [AvaloniaFact]
    public async Task ToolbarAndRowCopyUseTheirOwnScope()
    {
        using var context = new DanmuForwardViewTestContext();
        await AddRulesAsync(context, 2);
        using var selection = new WorkbenchContext(context.ViewModel, context.Room);
        using var view = new ForwardingPanelView(selection);
        var window = CreateWindow(view);

        try
        {
            window.Show();
            Pump(window);
            Click(window, Find<Button>(view, "ForwardCopyButton"));
            Assert.Equal(["42", "84", "126"], await CopiedUidsAsync(window));

            Click(window, Find<Button>(view, "ForwardDragHandle", context.Room.ForwardRules[2]));
            Click(window, Find<Button>(view, "ForwardDragHandle", context.Room.ForwardRules[0]));
            Click(window, Find<Button>(view, "ForwardCopyButton"));
            Assert.Equal(["42", "126"], await CopiedUidsAsync(window));
            Click(window, Find<Button>(view, "ForwardCopyRuleButton", context.Room.ForwardRules[1]));
            Assert.Equal(["84"], await CopiedUidsAsync(window));
            Assert.Equal(2, SelectedCards(view).Count());
            Assert.Equal(3, context.Room.ForwardRules.Count);

            Click(window, Find<Button>(view, "ForwardDragHandle", context.Room.ForwardRules[0]));
            Click(window, Find<Button>(view, "ForwardCopyButton"));
            Assert.Equal(["126"], await CopiedUidsAsync(window));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 粘贴按顺序追加禁用的新规则，错误剪贴板不改变既有规则。
    /// </summary>
    [AvaloniaFact]
    public async Task PasteAppendsFreshDisabledRulesAndRejectsInvalidClipboard()
    {
        using var context = new DanmuForwardViewTestContext();
        using var selection = new WorkbenchContext(context.ViewModel, context.Room);
        using var view = new ForwardingPanelView(selection);
        var window = CreateWindow(view);

        try
        {
            window.Show();
            Pump(window);
            var originalRule = context.Room.ForwardRules[0];
            var originalId = originalRule.Id;
            var payload = DanmuForwardRuleClipboardService.Serialize([originalRule.State, originalRule.State]);
            await window.Clipboard!.SetTextAsync(payload);
            Click(window, Find<Button>(view, "ForwardPasteButton"));
            await WaitForAsync(window, () => Find<TextBlock>(view, "ForwardClipboardStatus").Text!.StartsWith("已粘贴"));

            Assert.Equal(3, context.Room.ForwardRules.Count);
            Assert.Same(originalRule, context.Room.ForwardRules[0]);
            Assert.Equal(3, context.Room.ForwardRules.Select(rule => rule.Id).Distinct().Count());
            Assert.All(context.Room.ForwardRules.Skip(1), rule =>
            {
                Assert.False(rule.IsEnabled);
                Assert.NotEqual(originalId, rule.Id);
                Assert.Equal(originalRule.SourceRoomKey, rule.SourceRoomKey);
                Assert.Equal(originalRule.AccountOverrideId, rule.AccountOverrideId);
                Assert.Equal(originalRule.MarkSymbolGroupId, rule.MarkSymbolGroupId);
            });
            Assert.Empty(context.SocketFactory.Requests);

            await window.Clipboard.SetTextAsync("这是一段普通文本");
            Click(window, Find<Button>(view, "ForwardPasteButton"));
            await WaitForAsync(window, () => Find<TextBlock>(view, "ForwardClipboardStatus").Classes.Contains("error"));
            Assert.Equal(3, context.Room.ForwardRules.Count);
            Assert.Equal(payload, DanmuForwardRuleClipboardService.Serialize([originalRule.State, originalRule.State]));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 不连续多选通过真实指针拖到末尾，编辑和条目按钮不改变选择。
    /// </summary>
    [AvaloniaFact]
    public async Task HandleDragsSelectedGroupAndOtherControlsKeepSelection()
    {
        using var context = new DanmuForwardViewTestContext();
        await AddRulesAsync(context, 3);
        using var selection = new WorkbenchContext(context.ViewModel, context.Room);
        using var view = new ForwardingPanelView(selection);
        var window = CreateWindow(view, height: 900);

        try
        {
            window.Show();
            Pump(window);
            var rules = context.Room.ForwardRules.ToArray();
            Click(window, Find<Button>(view, "ForwardDragHandle", rules[0]));
            Click(window, Find<Button>(view, "ForwardDragHandle", rules[2]));
            Click(window, Find<TextBox>(view, "ForwardUidInput", rules[1]));
            Find<ComboBox>(view, "ForwardAccountSelector", rules[1]).IsDropDownOpen = true;
            Pump(window);
            Find<ComboBox>(view, "ForwardAccountSelector", rules[1]).IsDropDownOpen = false;
            Click(window, Find<Button>(view, "ForwardCopyRuleButton", rules[1]));
            Assert.Equal(2, SelectedCards(view).Count());
            var handle = Find<Button>(view, "ForwardDragHandle", rules[0]);
            var last = Find<Border>(view, "ForwardRuleCard", rules[3]);
            var start = Center(handle, window);
            var end = last.TranslatePoint(new Point(last.Bounds.Width / 2, last.Bounds.Height - 1), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end);
            Pump(window);
            Assert.Equal(rules, context.Room.ForwardRules);
            Assert.Contains(view.GetVisualDescendants().OfType<Border>(),
                border => border.Name == "ForwardDropIndicator" && border.IsVisible);
            window.MouseUp(end, MouseButton.Left);
            Pump(window);

            Assert.Equal([rules[1], rules[3], rules[0], rules[2]], context.Room.ForwardRules);
            Assert.Equal(context.Room.ForwardRules.Select(rule => rule.State), context.Room.State.ForwardRules);
            Assert.Equal(2, SelectedCards(view).Count());
            Assert.Empty(context.SocketFactory.Requests);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 短距离点击不排序，Escape 或切房会取消进行中的拖放。
    /// </summary>
    [AvaloniaFact]
    public async Task ShortClickEscapeAndRoomSwitchRespectGestureLifetime()
    {
        using var context = new DanmuForwardViewTestContext();
        await AddRulesAsync(context, 1);
        using var selection = new WorkbenchContext(context.ViewModel, context.Room);
        using var view = new ForwardingPanelView(selection);
        var window = CreateWindow(view);

        try
        {
            window.Show();
            Pump(window);
            var rules = context.Room.ForwardRules.ToArray();
            var handle = Find<Button>(view, "ForwardDragHandle", rules[0]);
            var start = Center(handle, window);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(1, 1));
            window.MouseUp(start + new Vector(1, 1), MouseButton.Left);
            Pump(window);
            Assert.Single(SelectedCards(view));
            Assert.Equal(rules, context.Room.ForwardRules);

            var last = Find<Border>(view, "ForwardRuleCard", rules[1]);
            var end = last.TranslatePoint(new Point(10, last.Bounds.Height - 1), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.MouseUp(end, MouseButton.Left);
            Pump(window);
            Assert.Equal(rules, context.Room.ForwardRules);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end);
            selection.SetPreviewRoom(context.Source);
            Pump(window);
            window.MouseUp(end, MouseButton.Left);
            selection.SetPreviewRoom(context.Room);
            Pump(window);
            Assert.Equal(rules, context.Room.ForwardRules);
            Assert.Empty(SelectedCards(view));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 启用、删除与添加各自生效，删除选中项后清理选择且保留其他监听。
    /// </summary>
    [AvaloniaFact]
    public async Task EnableDeleteAndAddKeepSelectionAndSessionConsistent()
    {
        using var context = new DanmuForwardViewTestContext();
        await AddRulesAsync(context, 1);
        using var selection = new WorkbenchContext(context.ViewModel, context.Room);
        using var view = new ForwardingPanelView(selection);
        var window = CreateWindow(view);

        try
        {
            window.Show();
            Pump(window);
            var first = context.Room.ForwardRules[0];
            var second = context.Room.ForwardRules[1];
            Click(window, Find<Button>(view, "ForwardDragHandle", first));
            Click(window, Find<Button>(view, "ForwardDragHandle", second));
            Click(window, Find<ToggleButton>(view, "ForwardEnabledToggle", first));
            await context.SocketFactory.Socket.StartedTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(first.IsEnabled);
            Assert.Equal(2, SelectedCards(view).Count());

            Click(window, Find<Button>(view, "ForwardDeleteRuleButton", second));
            await WaitForAsync(window, () => context.Room.ForwardRules.Count == 1);
            Assert.Single(SelectedCards(view));
            Assert.Single(context.SocketFactory.Requests);
            Assert.False(context.SocketFactory.Socket.DisposedTask.IsCompleted);
            Click(window, Find<Button>(view, "ForwardCopyButton"));
            Assert.Equal(["42"], await CopiedUidsAsync(window));

            Click(window, Find<Button>(view, "ForwardDeleteRuleButton", first));
            await context.SocketFactory.Socket.DisposedTask.WaitAsync(TimeSpan.FromSeconds(2));
            Pump(window);
            Assert.Empty(context.Room.ForwardRules);
            Assert.Empty(SelectedCards(view));
            Assert.False(Find<Button>(view, "ForwardCopyButton").IsEnabled);
            Click(window, Find<Button>(view, "ForwardAddButton"));
            await WaitForAsync(window, () => context.Room.ForwardRules.Count == 1);
            Assert.False(context.Room.ForwardRules[0].IsEnabled);
            Assert.Empty(SelectedCards(view));
        }
        finally
        {
            context.Room.StopForwarding();
            window.Close();

            if (context.SocketFactory.Socket.StartedTask.IsCompleted)
            {
                await context.SocketFactory.Socket.DisposedTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>
    /// 长列表拖动到边缘会自动滚动，释放后停止滚动并完成排序。
    /// </summary>
    [AvaloniaFact]
    public async Task DraggingAtViewportEdgeScrollsUntilReleased()
    {
        using var context = new DanmuForwardViewTestContext();
        await AddRulesAsync(context, 12);
        using var selection = new WorkbenchContext(context.ViewModel, context.Room);
        using var view = new ForwardingPanelView(selection);
        var window = CreateWindow(view, height: 420);

        try
        {
            window.Show();
            Pump(window);
            var first = context.Room.ForwardRules[0];
            var handle = Find<Button>(view, "ForwardDragHandle", first);
            var scroll = Find<ScrollViewer>(view, "ForwardRulesScrollViewer");
            var end = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height - 1), window)!.Value;
            window.MouseDown(Center(handle, window), MouseButton.Left);
            window.MouseMove(end);
            await WaitForAsync(window, () => scroll.Offset.Y > 0);
            window.MouseUp(end, MouseButton.Left);
            Pump(window);
            Assert.NotSame(first, context.Room.ForwardRules[0]);
            var stoppedOffset = scroll.Offset;
            await Task.Delay(100);
            Pump(window);
            Assert.Equal(stoppedOffset, scroll.Offset);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 不同宽度和主题中保持四行、方形启用按钮以及框内用户名，可输出真实渲染预览。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(320, false)]
    [InlineData(480, false)]
    [InlineData(860, false)]
    [InlineData(320, true)]
    [InlineData(480, true)]
    [InlineData(860, true)]
    public async Task CompactLayoutFitsWithUserNameInsideInput(double width, bool dark)
    {
        const string USER_NAME = "很长的同传用户名用于验证输入框内徽标省略";
        var service = new FakeBilibiliUserNameService
        {
            RespondAsync = (_, _) => Task.FromResult(USER_NAME)
        };
        using var context = new DanmuForwardViewTestContext(service);
        await AddRulesAsync(context, 1);
        using var selection = new WorkbenchContext(context.ViewModel, context.Room);
        using var view = new ForwardingPanelView(selection);
        var window = CreateWindow(view, width, height: 420);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        try
        {
            window.Show();
            await Task.WhenAll(context.Room.ForwardRules.Select(rule => rule.UserNameLookupTask));
            Pump(window);
            var rule = context.Room.ForwardRules[0];
            var card = Find<Border>(view, "ForwardRuleCard", rule);
            var grid = Assert.IsType<Grid>(card.Child);
            Assert.Equal(4, grid.RowDefinitions.Count);
            var enabled = Find<ToggleButton>(view, "ForwardEnabledToggle", rule);
            Assert.Equal(32, enabled.Bounds.Width);
            Assert.Equal(enabled.Bounds.Width, enabled.Bounds.Height);
            var uid = Find<TextBox>(view, "ForwardUidInput", rule);
            var pattern = Find<TextBox>(view, "ForwardPatternInput", rule);
            var badge = Assert.IsType<Border>(uid.InnerRightContent);
            var name = Find<TextBlock>(view, "ForwardSenderUserNameText", rule);
            Assert.Equal(USER_NAME, name.Text);
            Assert.Equal(USER_NAME, ToolTip.GetTip(name));
            Assert.True(badge.IsVisible);
            Assert.InRange(name.Bounds.Width, 1, 100);
            var badgeOrigin = badge.TranslatePoint(default, uid)!.Value;
            Assert.InRange(badgeOrigin.X, 1, uid.Bounds.Width - badge.Bounds.Width);
            Assert.True(uid.Bounds.Width >= 100);
            Assert.True(pattern.Bounds.Width >= 100);

            foreach (var control in card.GetVisualDescendants().OfType<Control>()
                         .Where(control => control is TextBox or ComboBox or Button))
            {
                var origin = control.TranslatePoint(default, card)!.Value;
                Assert.InRange(origin.X, -0.1, card.Bounds.Width + 0.1);
                Assert.True(origin.X + control.Bounds.Width <= card.Bounds.Width + 0.1,
                    $"{control.Name}: {origin.X} + {control.Bounds.Width} > {card.Bounds.Width}");
            }

            var directory = Environment.GetEnvironmentVariable("YOHUKE_FORWARD_QA_OUTPUT");

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                window.MouseMove(new(-1, -1));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, $"forwarding-{width}-{(dark ? "dark" : "light")}.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task AddRulesAsync(DanmuForwardViewTestContext context, int count)
    {
        for (var index = 0; index < count; index++)
        {
            await context.Room.AddForwardRuleStateAsync(new()
            {
                SenderUid = ((index + 2) * 42).ToString(), ContentPattern = "^【.*?】$",
                SourceWorkspaceId = "workspace", SourceRoomStateId = "source-room",
                AccountOverrideId = "forward-account", MarkSymbolGroupId = "forward-group"
            });
        }
    }

    private static Window CreateWindow(Control view, double width = 600, double height = 800)
    {
        return new()
        {
            Content = view, Width = width, Height = height
        };
    }

    private static void AssertIconContrast(LucideIcon icon, ContentPresenter presenter, Color surface)
    {
        Assert.True(icon.IsEffectivelyVisible);
        Assert.True(icon.Bounds.Width > 0 && icon.Bounds.Height > 0);
        var background = Composite(Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background), surface);
        var foreground = Composite(Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground), background);
        var backgroundLuminance = Luminance(background);
        var foregroundLuminance = Luminance(foreground);
        var contrast = (Math.Max(backgroundLuminance, foregroundLuminance) + 0.05) /
                       (Math.Min(backgroundLuminance, foregroundLuminance) + 0.05);
        Assert.True(contrast >= 2, $"电源图标与背景的对比度过低：{contrast:F2}，前景 {foreground}，背景 {background}");
    }

    private static Color Composite(ISolidColorBrush brush, Color background)
    {
        var alpha = brush.Color.A / 255d * brush.Opacity;
        return Color.FromRgb(
            (byte)Math.Round(brush.Color.R * alpha + background.R * (1 - alpha)),
            (byte)Math.Round(brush.Color.G * alpha + background.G * (1 - alpha)),
            (byte)Math.Round(brush.Color.B * alpha + background.B * (1 - alpha)));
    }

    private static double Luminance(Color color)
    {
        return 0.2126 * LinearChannel(color.R) + 0.7152 * LinearChannel(color.G) + 0.0722 * LinearChannel(color.B);
    }

    private static double LinearChannel(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static void SaveIconFrame(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_FORWARD_QA_OUTPUT");

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

    private static T Find<T>(Control view, string name, DanmuForwardRuleViewModel? rule = null) where T : Control
    {
        return view.GetVisualDescendants().OfType<T>().Single(control => control.Name == name &&
            (rule is null || ReferenceEquals(control.DataContext, rule)));
    }

    private static IEnumerable<Border> SelectedCards(Control view)
    {
        return view.GetVisualDescendants().OfType<Border>()
            .Where(card => card.Name == "ForwardRuleCard" && card.Classes.Contains("selected"));
    }

    private static async Task<string[]> CopiedUidsAsync(Window window)
    {
        var text = await window.Clipboard!.TryGetTextAsync();
        return DanmuForwardRuleClipboardService.Deserialize(text!).Select(rule => rule.SenderUid).ToArray();
    }

    private static Point Center(Control control, Window window)
    {
        return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    }

    private static void Click(Window window, Control control)
    {
        var point = Center(control, window);
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Pump(window);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static async Task WaitForAsync(Window window, Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);

        while (!predicate())
        {
            Assert.True(DateTime.UtcNow < deadline, "面板操作未在预期时间内完成。");
            await Task.Delay(10);
            Pump(window);
        }
    }
}
