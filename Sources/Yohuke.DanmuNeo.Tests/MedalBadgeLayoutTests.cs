using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 在正式列表与测试窗口中验证粉丝牌等宽及数字实际排版居中。
/// </summary>
public class MedalBadgeLayoutTests
{
    /// <summary>
    /// 不同名称长度和个位、两位、三位等级不改变徽章宽度，等级文字在同一数字区域居中。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void BadgesHaveFixedWidthAndCenteredLevel(bool preview, bool compact, bool dark)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var items = new[]
        {
            new DanmuFeedItem { MedalName = "牵丝蝠", MedalLevel = 8, MedalGuardLevel = 0 },
            new DanmuFeedItem { MedalName = "牵丝蝠", MedalLevel = 44, MedalGuardLevel = 2 },
            new DanmuFeedItem { MedalName = "牵丝蝠", MedalLevel = 120, MedalGuardLevel = 3 },
            new DanmuFeedItem { MedalName = "肉", MedalLevel = 1, MedalGuardLevel = 1 },
            new DanmuFeedItem { MedalName = "肉肉", MedalLevel = 25 },
            new DanmuFeedItem { MedalName = "四字牌名", MedalLevel = 9 }
        };
        Window window;

        if (preview)
        {
            var previewWindow = new DanmuStylePreviewWindow();
            var viewModel = Assert.IsType<DanmuStylePreviewViewModel>(previewWindow.DataContext);
            viewModel.IsCompactDanmuDisplay = compact;
            viewModel.ClearSamples();

            foreach (var item in items)
            {
                item.UserName = "测试用户";
                item.Content = "粉丝牌等宽测试";
                viewModel.DanmuItems.Add(item);
            }

            window = previewWindow;
        }
        else
        {
            context.ViewModel.IsCompactDanmuDisplay = compact;
            context.Room.DanmuItems.Clear();

            foreach (var item in items)
            {
                item.UserName = "测试用户";
                item.Content = "粉丝牌等宽测试";
                context.Room.DanmuItems.Add(item);
            }

            window = new Window
            {
                Width = 1100, Height = 800, DataContext = context.ViewModel,
                Content = new LiveRoomWorkspaceView { DataContext = context.Room }
            };
        }

        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var badges = window.GetVisualDescendants().OfType<Border>()
                .Where(control => control.Classes.Contains("danmuMedalBadge")).ToList();
            Assert.Equal(items.Length, badges.Count);
            var width = badges[0].Bounds.Width;
            Assert.All(badges, badge => Assert.InRange(badge.Bounds.Width, width - 0.01, width + 0.01));
            var levelCenters = new List<double>();

            foreach (var badge in badges)
            {
                var item = Assert.IsType<DanmuFeedItem>(badge.DataContext);
                var level = badge.GetVisualDescendants().OfType<TextBlock>()
                    .Single(control => control.Classes.Contains("danmuMedalLevel"));
                var textRange = Assert.Single(level.TextLayout.HitTestTextRange(0, level.Text!.Length));
                Assert.InRange(textRange.Center.X, level.Bounds.Width / 2 - 0.5, level.Bounds.Width / 2 + 0.5);
                Assert.True(textRange.Width <= level.Bounds.Width + 0.01);
                levelCenters.Add(level.TranslatePoint(new Point(textRange.Center.X, 0), badge)!.Value.X);
                Assert.Equal(item.MedalLevel.ToString(), level.Text);
                Assert.Equal(item.MedalText, ToolTip.GetTip(badge));
            }

            Assert.All(levelCenters, center => Assert.InRange(center, levelCenters[0] - 0.5, levelCenters[0] + 0.5));
            var directory = Environment.GetEnvironmentVariable("YOHUKE_FEED_QA_OUTPUT");

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory,
                    $"fixed-medal-{(preview ? "preview" : "feed")}-{(compact ? "compact" : "regular")}-{(dark ? "dark" : "light")}.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }
}
