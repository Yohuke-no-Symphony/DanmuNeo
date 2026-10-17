using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证日志面板的原生边界、窄布局、复制和展开详情。
/// </summary>
public class LogPanelViewTests
{
    /// <summary>供 XAML 加载器使用的空构造不创建会话服务或业务视图模型。</summary>
    [AvaloniaFact]
    public void ParameterlessDesignerViewHasNoSessionState()
    {
        using var view = new LogPanelView();
        Assert.Null(view.DataContext);
        Assert.False(view.IsEnabled);
    }

    /// <summary>隐藏、最小化、脱离视觉树时不清未读，恢复或迁入浮窗后重新识别阅读状态。</summary>
    [AvaloniaFact]
    public void ReadingRequiresVisibleAttachedNonMinimizedWindow()
    {
        using var log = new AppLogService();
        using var view = new LogPanelView(log);
        var window = new Window { Width = 520, Height = 360, Content = view };
        var floating = new Window { Width = 420, Height = 300 };

        try
        {
            log.Error("监听 / 房间 100", "显示前故障");
            Assert.Equal(1, log.UnreadErrorCount);
            Show(window);
            Assert.Equal(0, log.UnreadErrorCount);

            view.IsVisible = false;
            log.Error("监听", "隐藏时故障");
            Pump(window);
            Assert.Equal(1, log.UnreadErrorCount);
            view.IsVisible = true;
            Pump(window);
            Assert.Equal(0, log.UnreadErrorCount);

            window.WindowState = WindowState.Minimized;
            log.Error("监听", "最小化时故障");
            Pump(window);
            Assert.Equal(1, log.UnreadErrorCount);
            window.WindowState = WindowState.Normal;
            window.Activate();
            Pump(window);
            Assert.Equal(0, log.UnreadErrorCount);

            window.Content = null;
            log.Error("监听", "移动时故障");
            Pump(window);
            Assert.Equal(1, log.UnreadErrorCount);
            floating.Content = view;
            Show(floating);
            Assert.Equal(0, log.UnreadErrorCount);
        }
        finally
        {
            floating.Close();
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>面板在窄宽和深浅主题下保留可操作工具条，详情展开及筛选复制包含异常全文。</summary>
    [AvaloniaTheory]
    [InlineData(240, false)]
    [InlineData(240, true)]
    [InlineData(600, false)]
    [InlineData(600, true)]
    public async Task NarrowToolbarAndClipboardKeepCompleteDetails(int width, bool dark)
    {
        using var log = new AppLogService();
        log.Information("保存", "已保存工作区");
        log.Error("发送 / 房间 100", "发送失败", new IOException("用于展开与复制的诊断详情"));
        using var view = new LogPanelView(log);
        var viewModel = Assert.IsType<LogPanelViewModel>(view.DataContext);
        var window = new Window
        {
            Width = width,
            Height = 460,
            Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };

        try
        {
            Show(window);
            var filter = view.FindControl<ComboBox>("LogLevelFilter")!;
            var search = view.FindControl<TextBox>("LogSearch")!;
            var copySelected = view.FindControl<Button>("CopyLogSelected")!;
            var copyVisible = view.FindControl<Button>("CopyLogVisible")!;
            var clear = view.FindControl<Button>("ClearLog")!;

            foreach (var control in new Control[] { filter, search, copySelected, copyVisible, clear })
            {
                var point = control.TranslatePoint(default, view)!.Value;
                Assert.True(control.IsEffectivelyVisible);
                Assert.InRange(point.X, 0, view.Bounds.Width);
                Assert.True(point.X + control.Bounds.Width <= view.Bounds.Width + 0.5);
            }

            var expander = Assert.Single(view.GetVisualDescendants().OfType<Expander>(),
                control => control.IsEffectivelyVisible);
            Assert.False(expander.IsExpanded);
            expander.IsExpanded = true;
            Pump(window);
            var details = Assert.Single(expander.GetVisualDescendants().OfType<SelectableTextBlock>(),
                control => control.IsEffectivelyVisible);
            Assert.Contains("用于展开与复制的诊断详情", details.Text);

            viewModel.SelectedEntry = viewModel.Entries[1];
            Pump(window);
            copySelected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(viewModel.SelectedEntry.FullText, await window.Clipboard!.TryGetTextAsync());
            viewModel.FilterIndex = 3;
            Pump(window);
            copyVisible.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var copied = await window.Clipboard!.TryGetTextAsync();
            Assert.Contains("用于展开与复制的诊断详情", copied);
            Assert.DoesNotContain("已保存工作区", copied);
            Capture(window, $"log-{width}-{(dark ? "dark" : "light")}");

            clear.Command!.Execute(null);
            Pump(window);
            Assert.Empty(log.Entries);
            Assert.True(view.FindControl<TextBlock>("LogEmptyLabel")!.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>释放视图只解除订阅，应用仍能写入共享会话日志。</summary>
    [AvaloniaFact]
    public void DisposeOnlyReleasesViewSubscriptions()
    {
        using var log = new AppLogService();
        var view = new LogPanelView(log);
        var viewModel = Assert.IsType<LogPanelViewModel>(view.DataContext);
        view.Dispose();
        log.Error("监听", "关闭面板后故障");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(viewModel.Entries);
        Assert.Single(log.Entries);
        Assert.Equal(1, log.UnreadErrorCount);
    }

    private static void Show(Window window)
    {
        window.Show();
        window.Activate();
        Pump(window);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_LOG_QA_OUTPUT");

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"{name}.png"));
    }
}
