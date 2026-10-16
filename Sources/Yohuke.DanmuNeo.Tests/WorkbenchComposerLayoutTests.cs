using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证独立输入面板在拉高、收窄及显示错误时的真实编辑区域和操作区布局。
/// </summary>
public class WorkbenchComposerLayoutTests
{
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
            Assert.True(input.Bounds.Height >= 64);
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
            Assert.True(composer.Bounds.Height >= 124);
            Assert.True(composer.Input.Bounds.Height >= 64);
        }
        finally
        {
            window.Close();
        }
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
