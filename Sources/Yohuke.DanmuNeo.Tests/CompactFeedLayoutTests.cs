using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证普通弹幕与醒目留言在正式界面和预览中的独立换行选项。
/// </summary>
public class CompactFeedLayoutTests
{
    private const string LONG_CONTENT =
        "这是一段需要完整展示的长中文内容，用来检查自动换行选项是否只影响对应的消息类型。" +
        "缩小可用宽度之后仍然应该能够阅读完整内容，关闭换行则保留原来的单行滚动显示。";

    /// <summary>
    /// 紧凑模式下的四种开关组合只改变各自消息类型的实际排版。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void CompactWrapOptionsOnlyAffectTheirOwnFeed(bool preview, bool danmuWrap, bool superChatWrap)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var danmu = CreateDanmu();
        var superChat = CreateSuperChat();
        var window = CreateWindow(context, danmu, superChat, preview, true, danmuWrap, superChatWrap);

        try
        {
            ShowWindow(window);

            AssertWrapping(window, danmu, danmu.Content, danmuWrap);
            AssertWrapping(window, superChat, superChat.Content, superChatWrap);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 宽松模式始终完整换行，并保留两项紧凑模式偏好。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void WideModeAlwaysWrapsRegardlessOfCompactOptions(bool preview, bool danmuWrap, bool superChatWrap)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var danmu = CreateDanmu();
        var superChat = CreateSuperChat();
        var window = CreateWindow(context, danmu, superChat, preview, false, danmuWrap, superChatWrap);

        try
        {
            ShowWindow(window);

            AssertWrapping(window, danmu, danmu.Content, true);
            AssertWrapping(window, superChat, superChat.Content, true);

            SetCompactMode(context, window, true);
            PumpLayout(window);

            AssertWrapping(window, danmu, danmu.Content, danmuWrap);
            AssertWrapping(window, superChat, superChat.Content, superChatWrap);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 显示期间依次开启和关闭两项选项，另一种消息的排版保持不变。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SwitchingWrapOptionsUpdatesOnlyItsOwnContent(bool preview)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var danmu = CreateDanmu();
        var superChat = CreateSuperChat();
        var window = CreateWindow(context, danmu, superChat, preview, true, false, false);

        try
        {
            ShowWindow(window);

            foreach (var options in new[] { (false, true), (true, true), (true, false), (false, false) })
            {
                SetWrapOptions(context, window, options.Item1, options.Item2);
                PumpLayout(window);

                AssertWrapping(window, danmu, danmu.Content, options.Item1);
                AssertWrapping(window, superChat, superChat.Content, options.Item2);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 设置页复选框真实回写两项偏好，宽松模式禁用选项但不清空值。
    /// </summary>
    [AvaloniaFact]
    public void SettingsCheckBoxesRemainIndependentAcrossModes()
    {
        using var context = new LiveRoomFeedViewTestContext();
        context.ViewModel.IsCompactDanmuDisplay = false;
        var view = new GlobalSettingsView { DataContext = context.ViewModel };
        var window = new Window { Width = 1000, Height = 900, DataContext = context.ViewModel, Content = view };

        try
        {
            ShowWindow(window);
            var compact = view.GetVisualDescendants().OfType<CheckBox>()
                .Single(control => control.Content is "紧凑弹幕显示");
            var danmu = view.FindControl<CheckBox>("CompactDanmuWrapCheckBox")!;
            var superChat = view.FindControl<CheckBox>("CompactSuperChatWrapCheckBox")!;
            Assert.False(danmu.IsEffectivelyEnabled);
            Assert.False(superChat.IsEffectivelyEnabled);
            Assert.False(danmu.IsChecked);
            Assert.True(superChat.IsChecked);

            Click(window, compact);
            Assert.True(danmu.IsEffectivelyEnabled);
            Assert.True(superChat.IsEffectivelyEnabled);

            Click(window, danmu);
            Assert.True(context.ViewModel.IsCompactDanmuWrap);
            Assert.True(context.ViewModel.IsCompactSuperChatWrap);

            Click(window, superChat);
            Assert.True(context.ViewModel.IsCompactDanmuWrap);
            Assert.False(context.ViewModel.IsCompactSuperChatWrap);

            Click(window, compact);
            Assert.False(danmu.IsEffectivelyEnabled);
            Assert.False(superChat.IsEffectivelyEnabled);
            Assert.True(danmu.IsChecked);
            Assert.False(superChat.IsChecked);

            Click(window, compact);
            Assert.True(danmu.IsChecked);
            Assert.False(superChat.IsChecked);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 预览的独立选项在最窄窗口中仍可操作，宽松模式下保留禁用值。
    /// </summary>
    [AvaloniaFact]
    public void PreviewCheckBoxesFitNarrowWindowAndKeepPreferences()
    {
        var window = new DanmuStylePreviewWindow { Width = 820 };
        var viewModel = Assert.IsType<DanmuStylePreviewViewModel>(window.DataContext);

        try
        {
            ShowWindow(window);
            var compact = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(control => control.Content is "紧凑模式");
            var danmu = window.FindControl<CheckBox>("CompactDanmuWrapCheckBox")!;
            var superChat = window.FindControl<CheckBox>("CompactSuperChatWrapCheckBox")!;
            var theme = window.FindControl<ComboBox>("ThemeModeComboBox")!;

            foreach (var control in new Control[] { compact, danmu, superChat, theme })
            {
                var left = control.TranslatePoint(default, window)!.Value.X;
                Assert.InRange(left, 0, window.Bounds.Width);
                Assert.True(left + control.Bounds.Width <= window.Bounds.Width);
            }

            Assert.False(danmu.IsEffectivelyEnabled);
            Assert.False(superChat.IsEffectivelyEnabled);
            Click(window, compact);
            Click(window, danmu);
            Click(window, superChat);

            Assert.True(viewModel.IsCompactDanmuWrap);
            Assert.False(viewModel.IsCompactSuperChatWrap);

            Click(window, compact);
            Assert.False(danmu.IsEffectivelyEnabled);
            Assert.False(superChat.IsEffectivelyEnabled);
            Assert.True(danmu.IsChecked);
            Assert.False(superChat.IsChecked);
        }
        finally
        {
            window.Close();
        }
    }

    private static DanmuFeedItem CreateDanmu()
    {
        return new() { UserName = "弹幕测试用户", Content = $"普通弹幕：{LONG_CONTENT}" };
    }

    private static SuperChatItem CreateSuperChat()
    {
        return new() { UserName = "SC 测试用户", PriceText = "¥30", Content = $"醒目留言：{LONG_CONTENT}" };
    }

    private static Window CreateWindow(LiveRoomFeedViewTestContext context, DanmuFeedItem danmu,
        SuperChatItem superChat, bool preview, bool compact, bool danmuWrap, bool superChatWrap)
    {
        if (preview)
        {
            var window = new DanmuStylePreviewWindow { Width = 1100, Height = 840 };
            var viewModel = Assert.IsType<DanmuStylePreviewViewModel>(window.DataContext);
            viewModel.ClearSamples();
            viewModel.IsCompactDanmuDisplay = compact;
            viewModel.IsCompactDanmuWrap = danmuWrap;
            viewModel.IsCompactSuperChatWrap = superChatWrap;
            viewModel.DanmuItems.Add(danmu);
            viewModel.SuperChats.Add(superChat);
            return window;
        }

        context.ViewModel.IsCompactDanmuDisplay = compact;
        context.ViewModel.IsCompactDanmuWrap = danmuWrap;
        context.ViewModel.IsCompactSuperChatWrap = superChatWrap;
        context.Room.DanmuItems.Clear();
        context.Room.SuperChats.Clear();
        context.Room.DanmuItems.Add(danmu);
        context.Room.SuperChats.Add(superChat);
        return new()
        {
            Width = 1280,
            Height = 840,
            DataContext = context.ViewModel,
            Content = new LiveRoomWorkspaceView { DataContext = context.Room }
        };
    }

    private static void SetCompactMode(LiveRoomFeedViewTestContext context, Window window, bool compact)
    {
        if (window.DataContext is DanmuStylePreviewViewModel previewViewModel)
        {
            previewViewModel.IsCompactDanmuDisplay = compact;
        }
        else
        {
            context.ViewModel.IsCompactDanmuDisplay = compact;
        }
    }

    private static void SetWrapOptions(LiveRoomFeedViewTestContext context, Window window, bool danmuWrap,
        bool superChatWrap)
    {
        if (window.DataContext is DanmuStylePreviewViewModel previewViewModel)
        {
            previewViewModel.IsCompactDanmuWrap = danmuWrap;
            previewViewModel.IsCompactSuperChatWrap = superChatWrap;
        }
        else
        {
            context.ViewModel.IsCompactDanmuWrap = danmuWrap;
            context.ViewModel.IsCompactSuperChatWrap = superChatWrap;
        }
    }

    private static void AssertWrapping(Window window, object item, string content, bool wraps)
    {
        var button = window.GetVisualDescendants().OfType<Button>().Single(control =>
            ReferenceEquals(control.DataContext, item) &&
            (control.Classes.Contains("danmuFeedButton") || control.Classes.Contains("scFeedButton")));
        var text = button.GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.IsEffectivelyVisible && control.Text == content);

        Assert.Equal(wraps, text.TextLayout.TextLines.Count > 1);
        Assert.True(text.Bounds.Height >= text.TextLayout.Height - 0.5);
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        PumpLayout(window);
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
}
