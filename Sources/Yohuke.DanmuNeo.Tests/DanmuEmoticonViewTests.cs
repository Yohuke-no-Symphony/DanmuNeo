using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
/// 验证实际列表和 DEBUG 预览在所有布局中的表情图片与原文交互。
/// </summary>
public sealed class DanmuEmoticonViewTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 缓存就绪后绑定刷新为内联图片，重复表情不丢失，点击仍插入原始正文。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void CachedEmoticonsRenderInProductionAndPreview(bool preview, bool compact, bool wrap)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample.png");
        File.WriteAllBytes(path, EmoticonHttpHandler.ImageBytes);
        using var context = new LiveRoomFeedViewTestContext();
        var item = new DanmuFeedItem
        {
            UserName = "表情用户",
            UserUid = "42",
            Content = "前文[a.b]+中间[a.b]+[未知]尾文"
        };
        Window window;

        if (preview)
        {
            window = new DanmuStylePreviewWindow();
            var viewModel = Assert.IsType<DanmuStylePreviewViewModel>(window.DataContext);
            viewModel.ClearSamples();
            viewModel.IsCompactDanmuDisplay = compact;
            viewModel.IsCompactDanmuWrap = wrap;
            viewModel.DanmuItems.Add(item);
        }
        else
        {
            context.ViewModel.IsCompactDanmuDisplay = compact;
            context.ViewModel.IsCompactDanmuWrap = wrap;
            context.Room.DanmuItems.Clear();
            context.Room.DanmuItems.Add(item);
            window = new()
            {
                Width = 1100,
                Height = 800,
                DataContext = context.ViewModel,
                Content = new LiveRoomWorkspaceView(new(context.ViewModel, context.Room)) { DataContext = context.Room }
            };
        }

        try
        {
            window.Show();
            PumpLayout(window);
            item.Emoticons = [new() { Text = "[a.b]+", ImagePath = path }];
            PumpLayout(window);

            var content = window.GetVisualDescendants().OfType<EmoticonTextBlock>()
                .Single(control => ReferenceEquals(control.DataContext, item) && control.IsEffectivelyVisible);
            Assert.Equal(item.Content, content.ContentText);
            Assert.NotNull(content.Inlines);
            var images = content.Inlines.OfType<InlineUIContainer>()
                .Select(inline => Assert.IsType<Image>(inline.Child)).ToArray();
            Assert.Equal(2, images.Length);
            Assert.All(images, image =>
            {
                Assert.NotNull(image.Source);
                Assert.Equal(20, image.Height);
                Assert.True(image.Bounds.Width > 0);
            });
            Assert.Equal("前文中间[未知]尾文", string.Concat(content.Inlines.OfType<Run>().Select(run => run.Text)));

            if (!preview)
            {
                context.Room.InputDraft = "";
                var point = images[0].TranslatePoint(new Point(5, 5), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                PumpLayout(window);
                Assert.Contains(item.Content, context.Room.InputDraft);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 缺失和损坏的本地图片回退为可读表情文本，重新挂载控件后仍可显示。
    /// </summary>
    [AvaloniaFact]
    public void MissingAndCorruptImagesKeepTheirText()
    {
        Directory.CreateDirectory(directory);
        var corruptPath = Path.Combine(directory, "corrupt.png");
        File.WriteAllText(corruptPath, "not an image");
        var content = new EmoticonTextBlock
        {
            ContentText = "[缺失][损坏]",
            Emoticons =
            [
                new() { Text = "[缺失]", ImagePath = Path.Combine(directory, "missing.png") },
                new() { Text = "[损坏]", ImagePath = corruptPath }
            ]
        };
        var window = new Window { Content = content };

        try
        {
            window.Show();
            PumpLayout(window);
            Assert.NotNull(content.Inlines);
            Assert.Empty(content.Inlines.OfType<InlineUIContainer>());
            Assert.Equal(content.ContentText, string.Concat(content.Inlines.OfType<Run>().Select(run => run.Text)));
            window.Content = null;
            window.Content = content;
            PumpLayout(window);
            Assert.Equal(content.ContentText, string.Concat(content.Inlines!.OfType<Run>().Select(run => run.Text)));
        }
        finally
        {
            window.Close();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }

        GC.SuppressFinalize(this);
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
