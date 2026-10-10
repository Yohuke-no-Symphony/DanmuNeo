using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证 DEBUG 预览窗口的数据、生产样式和共享滚动行为。
/// </summary>
public class DanmuStylePreviewWindowTests
{
    /// <summary>
    /// 两种模式和主题下均显示粉丝牌、上舰用户名与档位，并可渲染窄窗口。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false, 1040)]
    [InlineData(true, true, 820)]
    public void PreviewShowsMedalAndGuardSamples(bool compact, bool dark, double width)
    {
        var window = new DanmuStylePreviewWindow
            { Width = width, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        var viewModel = Assert.IsType<DanmuStylePreviewViewModel>(window.DataContext);
        viewModel.IsCompactDanmuDisplay = compact;

        try
        {
            ShowWindow(window);
            var list = window.GetVisualDescendants().OfType<ListBox>().Single();
            list.ScrollIntoView(viewModel.SuperChats.Last());
            PumpLayout(window);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                control => control.Text == "总督" && control.IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                control => control.Text == "总督上舰用户" && control.IsEffectivelyVisible);
            var badges = window.GetVisualDescendants().OfType<Border>()
                .Where(control => control.Classes.Contains("danmuMedalBadge")).ToList();
            Assert.Contains(badges,
                control => control.IsEffectivelyVisible && control.Child is TextBlock { Text: "星光 25" });
            Assert.Contains(badges, control => !control.IsVisible);
            var badge = badges.First(control => control.IsVisible);
            var danmu = badge.GetVisualAncestors().OfType<Button>().First();
            var name = danmu.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.Classes.Contains("danmuFeedUser"));
            var badgePosition = badge.TranslatePoint(default, danmu)!.Value;
            var namePosition = name.TranslatePoint(default, danmu)!.Value;
            Assert.True(badgePosition.X + badge.Bounds.Width <= namePosition.X);
            Assert.InRange(badgePosition.Y + badge.Bounds.Height / 2,
                namePosition.Y + name.Bounds.Height / 2 - 0.5, namePosition.Y + name.Bounds.Height / 2 + 0.5);
            var previewDirectory = Environment.GetEnvironmentVariable("YOHUKE_FEED_QA_OUTPUT");

            if (!string.IsNullOrWhiteSpace(previewDirectory))
            {
                Directory.CreateDirectory(previewDirectory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(
                    Path.Combine(previewDirectory, compact ? "debug-feed-compact.png" : "debug-feed-regular.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 批量按钮真实添加弹幕，下翻后新弹幕不拉回，返回顶部后恢复跟随。
    /// </summary>
    [AvaloniaFact]
    public void BatchButtonAndScrollMatchProductionBehavior()
    {
        var window = new DanmuStylePreviewWindow();
        var viewModel = Assert.IsType<DanmuStylePreviewViewModel>(window.DataContext);

        try
        {
            ShowWindow(window);
            var batch = window.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Content is "批量添加 80 条");
            batch.BringIntoView();
            PumpLayout(window);
            Click(window, batch);
            Assert.Equal(84, viewModel.DanmuItems.Count);
            PumpLayout(window);
            var scroll = window.FindControl<ScrollViewer>("DanmuScrollViewer")!;
            viewModel.AddDanmu();
            scroll.Offset = new Vector(0, 180);
            PumpLayout(window);
            Assert.True(scroll.Offset.Y > 1);
            scroll.Offset = default;
            viewModel.AddDanmu();
            PumpLayout(window);
            Assert.Equal(0, scroll.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 预览窗口输入框中的 Tab 沿用主窗口轮切绑定并展示新预设。
    /// </summary>
    [AvaloniaFact]
    public async Task PreviewTabUsesOwnerWorkspaceBinding()
    {
        using var context = new MarkGroupCycleTestContext();
        var owner = new MainWindow { DataContext = context.ViewModel };
        var window = new DanmuStylePreviewWindow();

        try
        {
            ShowWindow(owner);
            window.Show(owner);
            window.Activate();
            PumpLayout(window);
            var input = window.GetVisualDescendants().OfType<TextBox>().Single(control => control.Watermark is "测试用户");
            input.Focus();
            Assert.True(input.IsFocused, "按键之前应聚焦编辑框");
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Assert.True(input.IsFocused, "按键之后应保持编辑框焦点");
            await context.ViewModel.CycleWorkspaceMarkGroupCommand.ExecutionTask!;

            Assert.Equal("b", context.ViewModel.SelectedWorkspaceMarkGroup!.Id);
            Assert.True(input.IsFocused);
            Assert.Equal("方括号", window.FindControl<TextBlock>("CurrentMarkGroupText")!.Text);
            context.ViewModel.SelectedWorkspace = context.ViewModel.Workspaces[1];
            Assert.Equal("引号", window.FindControl<TextBlock>("CurrentMarkGroupText")!.Text);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            await context.ViewModel.CycleWorkspaceMarkGroupCommand.ExecutionTask!;
            Assert.Equal("a", context.ViewModel.SelectedWorkspaceMarkGroup!.Id);
            Assert.Equal("默认样式", window.FindControl<TextBlock>("CurrentMarkGroupText")!.Text);
        }
        finally
        {
            window.Close();
            owner.DataContext = null;
            owner.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 独立预览没有工作区时隐藏预设提示，Tab 保留普通焦点导航。
    /// </summary>
    [AvaloniaFact]
    public void StandalonePreviewKeepsTabNavigation()
    {
        var window = new DanmuStylePreviewWindow();

        try
        {
            ShowWindow(window);
            var input = window.GetVisualDescendants().OfType<TextBox>().Single(control => control.Watermark is "测试用户");
            input.Focus();
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);

            Assert.False(input.IsFocused);
            Assert.False(window.FindControl<StackPanel>("CurrentMarkGroupPanel")!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 两种模式下三档上舰均为单行、颜色不同，实际点击复制完整用户名。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task GuardRowsUseTierColorAndCopyUserName(bool compact, int index)
    {
        var window = new DanmuStylePreviewWindow();
        var viewModel = Assert.IsType<DanmuStylePreviewViewModel>(window.DataContext);
        viewModel.IsCompactDanmuDisplay = compact;
        viewModel.UserName = "这是一个很长的上舰用户名，用于检查复制完整内容";
        viewModel.SelectedGuardPreset = viewModel.GuardPresets[index];
        viewModel.AddGuardPurchase();
        var item = viewModel.SuperChats[0];

        try
        {
            ShowWindow(window);
            var button = window.GetVisualDescendants().OfType<Button>().Single(control =>
                control.Classes.Contains("scFeedButton") && ReferenceEquals(control.DataContext, item));
            var name = button.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.Classes.Contains("danmuFeedUser"));
            var tier = button.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.Classes.Contains("scFeedPrice"));
            var layout = button.GetVisualDescendants().OfType<Grid>()
                .Single(control => control.Classes.Contains("scFeedLayout"));
            Assert.Equal(0, layout.RowSpacing);
            Assert.InRange(layout.Bounds.Height, name.Bounds.Height - 1, name.Bounds.Height + 1);
            Assert.Equal(item.BorderColor, Assert.IsType<SolidColorBrush>(tier.Foreground).Color);
            Assert.Equal(2,
                button.GetVisualDescendants().OfType<TextBlock>().Count(control => control.IsEffectivelyVisible));
            await window.Clipboard!.SetTextAsync("原剪贴板");
            Click(window, button);
            Assert.Equal(viewModel.UserName, await window.Clipboard.TryGetTextAsync());
        }
        finally
        {
            window.Close();
        }
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
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
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!
            .Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
