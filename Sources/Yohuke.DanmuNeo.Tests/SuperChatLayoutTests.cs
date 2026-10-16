using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用正式列表与预览窗口验证醒目留言的多行排版。
/// </summary>
public class SuperChatLayoutTests
{
    private const string LONG_CONTENT =
        "这是一条较长的中文醒目留言，用来确认正文能够按照列表的可用宽度自动换行。" +
        "无论采用紧凑模式还是宽松模式，每一句内容都应完整显示，并且让卡片随着正文高度增长。" +
        "继续补充一段中文内容，检查调整列表宽度以后，文字会重新排版而不是被限制在单行中。";

    /// <summary>
    /// 两种列表与显示模式均按实际宽度自动换行，并完整容纳中文正文。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LongChineseContentWrapsAcrossLines(bool preview, bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var item = CreateItem(LONG_CONTENT);
        var window = CreateWindow(context, item, preview, compact);

        try
        {
            ShowWindow(window);
            var text = GetContentText(window, item);

            Assert.True(text.TextLayout.TextLines.Count > 1,
                "长中文 SC 应在当前列表宽度内自动换行。");
            AssertContentFitsCard(window, item, text);
            CapturePreview(window, preview, compact);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 正文中的不同换行符均保留为可见段落，卡片不裁剪后续行。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExplicitLineBreaksPreserveAllParagraphs(bool preview, bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var item = CreateItem("第一段醒目留言\r\n第二段继续感谢\n第三段完整显示");
        var window = CreateWindow(context, item, preview, compact);

        try
        {
            ShowWindow(window);
            var text = GetContentText(window, item);

            Assert.Equal(3, text.TextLayout.TextLines.Count);
            AssertContentFitsCard(window, item, text);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 列表可用宽度缩小时增加正文行数，并相应增加卡片高度。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NarrowerListIncreasesLineCountAndCardHeight(bool preview, bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var item = CreateItem(LONG_CONTENT);
        var window = CreateWindow(context, item, preview, compact);

        try
        {
            ShowWindow(window);
            var list = window.GetVisualDescendants().OfType<ListBox>()
                .Single(control => control.Classes.Contains("feedList"));
            list.Width = 340;
            PumpLayout(window);
            var text = GetContentText(window, item);
            var wideLineCount = text.TextLayout.TextLines.Count;
            var wideTextHeight = text.Bounds.Height;
            var wideCardHeight = GetCard(window, item).Bounds.Height;

            list.Width = 240;
            PumpLayout(window);
            text = GetContentText(window, item);

            Assert.True(text.TextLayout.TextLines.Count > wideLineCount,
                "缩窄列表后应重新换行并增加正文行数。");
            Assert.True(text.Bounds.Height > wideTextHeight);
            Assert.True(GetCard(window, item).Bounds.Height > wideCardHeight);
            AssertContentFitsCard(window, item, text);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 显示期间切换模式后，正文持续可见并保留完整多行布局。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SwitchingDisplayModeKeepsWrappedContentVisible(bool preview, bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var item = CreateItem(LONG_CONTENT);
        var window = CreateWindow(context, item, preview, compact);

        try
        {
            ShowWindow(window);

            foreach (var nextMode in new[] { !compact, compact })
            {
                if (window.DataContext is DanmuStylePreviewViewModel previewViewModel)
                {
                    previewViewModel.IsCompactDanmuDisplay = nextMode;
                }
                else
                {
                    context.ViewModel.IsCompactDanmuDisplay = nextMode;
                }

                PumpLayout(window);
                var text = GetContentText(window, item);

                Assert.True(text.TextLayout.TextLines.Count > 1,
                    "切换模式后的 SC 正文应持续使用多行排版。");
                AssertContentFitsCard(window, item, text);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static SuperChatItem CreateItem(string content)
    {
        return new()
        {
            Kind = LivePaidEventKind.SuperChat,
            UserName = "多行 SC 测试用户",
            PriceText = "¥30",
            Content = content
        };
    }

    private static Window CreateWindow(LiveRoomFeedViewTestContext context, SuperChatItem item, bool preview,
        bool compact)
    {
        if (preview)
        {
            var window = new DanmuStylePreviewWindow { Width = 1100, Height = 840 };
            var viewModel = Assert.IsType<DanmuStylePreviewViewModel>(window.DataContext);
            viewModel.ClearSamples();
            viewModel.IsCompactDanmuDisplay = compact;
            viewModel.IsCompactSuperChatWrap = true;
            viewModel.SuperChats.Add(item);
            return window;
        }

        context.ViewModel.IsCompactDanmuDisplay = compact;
        context.ViewModel.IsCompactSuperChatWrap = true;
        context.Room.SuperChats.Clear();
        context.Room.SuperChats.Add(item);
        return new()
        {
            Width = 1280,
            Height = 840,
            DataContext = context.ViewModel,
            Content = new LiveRoomWorkspaceView(new(context.ViewModel, context.Room)) { DataContext = context.Room }
        };
    }

    private static TextBlock GetContentText(Window window, SuperChatItem item)
    {
        return GetCard(window, item).GetVisualDescendants().OfType<TextBlock>()
            .Single(control => control.IsEffectivelyVisible && control.Text == item.Content);
    }

    private static Border GetCard(Window window, SuperChatItem item)
    {
        return window.GetVisualDescendants().OfType<Border>().Single(control =>
            control.Classes.Contains("scFeedCard") && ReferenceEquals(control.DataContext, item));
    }

    private static void AssertContentFitsCard(Window window, SuperChatItem item, TextBlock text)
    {
        Assert.Equal(item.Content, text.Text);
        Assert.True(text.Bounds.Height >= text.TextLayout.Height - 0.5,
            "正文实际高度应容纳全部文字行。");
        var card = GetCard(window, item);
        var bottom = text.TranslatePoint(new Point(0, text.Bounds.Height), card)!.Value.Y;
        Assert.True(bottom <= card.Bounds.Height + 0.5,
            "SC 卡片应容纳正文最后一行。");
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

    private static void CapturePreview(Window window, bool preview, bool compact)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_SC_QA_OUTPUT");

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory,
            $"sc-{(preview ? "preview" : "feed")}-{(compact ? "compact" : "regular")}.png"));
    }
}
