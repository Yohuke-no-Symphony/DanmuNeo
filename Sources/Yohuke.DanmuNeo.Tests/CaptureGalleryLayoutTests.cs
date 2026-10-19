using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Captures;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证图库紧凑列表、工具栏、主题与实际分栏交互。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class CaptureGalleryLayoutTests
{
    /// <summary>空备注不占行，缩略图居左，时间同行，选中底色柔和且宽屏分栏可拖动。</summary>
    [AvaloniaTheory]
    [InlineData(240, false, "zh-CN")]
    [InlineData(320, true, "en-US")]
    [InlineData(640, false, "zh-CN")]
    [InlineData(960, true, "zh-CN")]
    [InlineData(960, false, "en-US")]
    public async Task GalleryRowsStayCompactAndPanesCanResize(int width, bool dark, string language)
    {
        using var localization = new LocalizationTestScope();
        Localization.SetLanguage(language);
        using var fixture = new CaptureWorkbenchTestContext();
        await fixture.Application.StartToolsAsync();
        await fixture.Application.GalleryInitialization;
        var gallery = fixture.Application.LiveCaptures;
        var frame = CreateFrame();
        await gallery.CaptureAsync(frame, "测试直播间");
        await gallery.CaptureAsync(frame with { CapturedAt = frame.CapturedAt.AddMinutes(1) }, "另一直播间");
        await gallery.CaptureAsync(frame with { CapturedAt = frame.CapturedAt.AddMinutes(2) }, "名称较长的直播间");
        gallery.Captures[0].Note = "同传第一段\n待核对的备注";
        await gallery.FlushAsync();
        using var context = new WorkbenchContext(fixture.Application);
        using var panel = new LiveCapturesPanelView(context, gallery);
        var window = new Window
        {
            Width = width, Height = 680, Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var list = panel.FindControl<ListBox>("CaptureList")!;
            var divider = panel.FindControl<GridSplitter>("CaptureDivider")!;
            var preview = panel.FindControl<ScrollViewer>("CapturePreview")!;
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(list.Background).Color.A);
            var buttons = panel.FindControl<WrapPanel>("CaptureToolbar")!.Children.OfType<Button>()
                .Append(panel.FindControl<Button>("CaptureClearFilters")!).ToArray();
            Assert.Equal(6, buttons.Length);
            foreach (var button in buttons)
            {
                var icon = Assert.Single(button.GetVisualDescendants().OfType<LucideIcon>());
                var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
                Assert.False(string.IsNullOrWhiteSpace(label.Text));
                Assert.True(icon.TranslatePoint(default, button)!.Value.X < label.TranslatePoint(default, button)!.Value.X);
                Assert.InRange(button.TranslatePoint(new(button.Bounds.Width, 0), panel)!.Value.X, 0, width + 0.5);
            }
            foreach (var row in list.GetRealizedContainers().OfType<ListBoxItem>())
            {
                var item = Assert.IsType<LiveCaptureItemViewModel>(row.DataContext);
                var texts = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
                var title = texts.Single(text => text.Text == item.Title);
                var time = texts.Single(text => text.Text == item.ListTimeText);
                var note = texts.Single(text => text.Classes.Contains("captureNote"));
                var image = Assert.Single(row.GetVisualDescendants().OfType<LiveCaptureImage>());
                Assert.True(image.TranslatePoint(default, row)!.Value.X < title.TranslatePoint(default, row)!.Value.X);
                var titleCenter = title.TranslatePoint(new(0, title.Bounds.Height / 2), row)!.Value.Y;
                Assert.InRange(time.TranslatePoint(new(0, time.Bounds.Height / 2), row)!.Value.Y, titleCenter - 1, titleCenter + 1);
                Assert.Equal(item.HasNote, note.IsEffectivelyVisible);
                if (!item.HasNote)
                {
                    Assert.InRange(row.Bounds.Height, 44, 60);
                }
                Assert.Equal(item.CapturedAtText, ToolTip.GetTip(time));
            }
            var selected = list.GetRealizedContainers().OfType<ListBoxItem>().Single(row => row.IsSelected);
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(selected.GetVisualDescendants()
                .OfType<ContentPresenter>().Single().Background);
            Assert.InRange(fill.Opacity * fill.Color.A / 255d, 0.01, 0.2);
            if (width >= 640)
            {
                Assert.Equal(GridResizeDirection.Columns, divider.ResizeDirection);
                var oldListWidth = list.Bounds.Width;
                var oldPreviewWidth = preview.Bounds.Width;
                var point = divider.TranslatePoint(new(divider.Bounds.Width / 2, divider.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseMove(point + new Vector(40, 0), RawInputModifiers.LeftMouseButton);
                window.MouseUp(point + new Vector(40, 0), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(list.Bounds.Width > oldListWidth + 20);
                Assert.True(preview.Bounds.Width < oldPreviewWidth - 20);
            }
            else
            {
                Assert.Equal(GridResizeDirection.Rows, divider.ResizeDirection);
                Assert.Equal(0, Grid.GetColumn(preview));
            }
            var directory = Environment.GetEnvironmentVariable("YOHUKE_CAPTURE_GALLERY_QA_OUTPUT");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                window.MouseMove(new(-1, -1));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                using var rendered = window.CaptureRenderedFrame();
                Assert.NotNull(rendered);
                rendered.Save(Path.Combine(directory, $"gallery-{width}-{(dark ? "dark" : "light")}-{language}.png"));
            }
        }
        finally
        {
            window.Close();
            await gallery.FlushAsync();
        }
    }

    private static LiveCapturedFrame CreateFrame()
    {
        var scene = new Border
        {
            Width = 360, Height = 640, Background = new SolidColorBrush(Color.Parse("#244967")),
            Child = new TextBlock
            {
                Text = "LIVE\n离线预览", FontSize = 32, Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        scene.Measure(new(360, 640));
        scene.Arrange(new(0, 0, 360, 640));
        using var bitmap = new RenderTargetBitmap(new(360, 640), new(96, 96));
        bitmap.Render(scene);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return LiveCaptureStoreTests.CreateFrame() with { PngBytes = stream.ToArray(), Width = 360, Height = 640 };
    }
}
