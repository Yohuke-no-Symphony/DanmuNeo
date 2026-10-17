using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证窗口内容自适应接管、原生客户区回读与窗口生命周期。
/// </summary>
public sealed class WindowContentSizingTests
{
    /// <summary>
    /// 原生边框回读增加宽度时仍保留明确请求宽度，达到目标后重复布局不再请求缩放。
    /// </summary>
    [AvaloniaFact]
    public void HeightSizingKeepsRequestedWidthAcrossNativeReadbackAndRepeatedFit()
    {
        var content = new Border { Height = 120 };
        var window = CreateWindow(content);
        var nativeSize = new Size(476, 500);
        var requests = new List<Size>();
        using var sizing = new WindowContentSizing(window, () => { }, size =>
        {
            requests.Add(size);
            nativeSize = size;
            window.Width = size.Width;
            window.Height = size.Height;
        }, () => nativeSize);

        try
        {
            Assert.Equal(SizeToContent.Manual, window.SizeToContent);
            Assert.Same(content, Assert.IsType<WindowContentSizingPresenter>(window.Content).Child);
            window.Show();
            window.Width = nativeSize.Width;
            PumpLayout(window);

            Assert.True(sizing.NeedsResize);
            sizing.FitToContent();
            Assert.Equal(new Size(460, 120), Assert.Single(requests));
            Assert.Equal(new Size(460, 120), nativeSize);

            for (var index = 0; index < 12; index++)
            {
                PumpLayout(window);
                Assert.False(sizing.NeedsResize);
                sizing.FitToContent();
            }

            Assert.Single(requests);
            Assert.Equal(460, window.Width);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 内容高度变化后继续自适应，既能超出当前客户区增长也能重新缩小。
    /// </summary>
    [AvaloniaFact]
    public void ContentHeightCanGrowAndShrinkAfterFirstFit()
    {
        var content = new Border { Height = 120 };
        var window = CreateWindow(content);
        var nativeSize = new Size(460, 500);
        var requests = new List<Size>();
        using var sizing = new WindowContentSizing(window, () => { }, size =>
        {
            requests.Add(size);
            nativeSize = size;
            window.Width = size.Width;
            window.Height = size.Height;
        }, () => nativeSize);

        try
        {
            window.Show();
            PumpLayout(window);
            sizing.FitToContent();
            Assert.Equal(new Size(460, 120), nativeSize);

            content.Height = 340;
            PumpLayout(window);
            Assert.True(sizing.NeedsResize);
            sizing.FitToContent();
            Assert.Equal(new Size(460, 340), nativeSize);

            content.Height = 72;
            PumpLayout(window);
            Assert.True(sizing.NeedsResize);
            sizing.FitToContent();
            PumpLayout(window);

            Assert.Equal(new[] { new Size(460, 120), new Size(460, 340), new Size(460, 72) }, requests);
            Assert.False(sizing.NeedsResize);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 原生尺寸回写不覆盖固定宽度，业务层的新宽度请求仍改变后续目标。
    /// </summary>
    [AvaloniaFact]
    public void ExplicitWidthRequestTakesEffectAfterNativeSizeReadback()
    {
        var window = CreateWindow(new Border { Height = 120 });
        var nativeSize = new Size(476, 500);
        var requests = new List<Size>();
        using var sizing = new WindowContentSizing(window, () => { }, size =>
        {
            requests.Add(size);
            nativeSize = size;
            window.Width = size.Width;
            window.Height = size.Height;
        }, () => nativeSize);

        try
        {
            window.Show();
            window.Width = nativeSize.Width;
            PumpLayout(window);
            sizing.FitToContent();
            Assert.Equal(new Size(460, 120), nativeSize);

            window.Width = 620;
            PumpLayout(window);
            Assert.True(sizing.NeedsResize);
            sizing.FitToContent();
            PumpLayout(window);

            Assert.Equal(new[] { new Size(460, 120), new Size(620, 120) }, requests);
            Assert.False(sizing.NeedsResize);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 显式切换手动模式后恢复原始内容，重新启用自动高度与替换内容仍保持后续自适应。
    /// </summary>
    [AvaloniaFact]
    public void ManualModeRestoresContentAndAutomaticSizingCanResumeWithReplacement()
    {
        var content = new Border { Height = 120 };
        var window = CreateWindow(content);
        var nativeSize = new Size(460, 500);
        var requests = new List<Size>();
        using var sizing = new WindowContentSizing(window, () => { }, size =>
        {
            requests.Add(size);
            nativeSize = size;
            window.Width = size.Width;
            window.Height = size.Height;
        }, () => nativeSize);

        try
        {
            window.Show();
            PumpLayout(window);
            sizing.FitToContent();
            nativeSize = new(460, 240);
            window.Width = nativeSize.Width;
            window.Height = nativeSize.Height;
            sizing.RequestedMode = SizeToContent.Manual;
            PumpLayout(window);

            Assert.Equal(SizeToContent.Manual, window.SizeToContent);
            Assert.Same(content, window.Content);
            Assert.False(sizing.NeedsResize);
            content.Height = 300;
            PumpLayout(window);
            sizing.FitToContent();
            Assert.Single(requests);

            sizing.RequestedMode = SizeToContent.Height;
            PumpLayout(window);
            Assert.Equal(SizeToContent.Manual, window.SizeToContent);
            Assert.Same(content, Assert.IsType<WindowContentSizingPresenter>(window.Content).Child);
            sizing.FitToContent();
            Assert.Equal(new Size(460, 300), nativeSize);

            var replacement = new Border { Height = 180 };
            window.Content = replacement;
            PumpLayout(window);
            Assert.Same(replacement, Assert.IsType<WindowContentSizingPresenter>(window.Content).Child);
            sizing.FitToContent();
            PumpLayout(window);

            Assert.Equal(new[] { new Size(460, 120), new Size(460, 300), new Size(460, 180) }, requests);
            Assert.False(sizing.NeedsResize);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 用户只拖动固定宽度时保留自动高度，后续内容变化使用用户的新宽度。
    /// </summary>
    [AvaloniaFact]
    public void UserWidthResizeKeepsAutomaticHeightAndUpdatesFixedWidth()
    {
        var content = new Border { Height = 120 };
        var window = CreateWindow(content);
        var nativeSize = new Size(460, 500);
        var requests = new List<Size>();
        using var sizing = new WindowContentSizing(window, () => { }, size =>
        {
            requests.Add(size);
            nativeSize = size;
            window.Width = size.Width;
            window.Height = size.Height;
        }, () => nativeSize);

        try
        {
            window.Show();
            PumpLayout(window);
            sizing.FitToContent();
            nativeSize = new(620, 120);
            window.Width = nativeSize.Width;
            window.Height = nativeSize.Height;
            sizing.OnResized(nativeSize, WindowResizeReason.User);
            PumpLayout(window);

            Assert.Equal(SizeToContent.Height, sizing.RequestedMode);
            Assert.IsType<WindowContentSizingPresenter>(window.Content);
            Assert.False(sizing.NeedsResize);
            content.Height = 260;
            PumpLayout(window);
            sizing.FitToContent();
            PumpLayout(window);

            Assert.Equal(new[] { new Size(460, 120), new Size(620, 260) }, requests);
            Assert.False(sizing.NeedsResize);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 用户调整自动尺寸轴后只保留另一自动轴，完全手动时恢复原始内容。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(SizeToContent.Height, 460, 220, SizeToContent.Manual, 460, 220)]
    [InlineData(SizeToContent.WidthAndHeight, 540, 120, SizeToContent.Height, 540, 160)]
    [InlineData(SizeToContent.WidthAndHeight, 320, 220, SizeToContent.Width, 360, 220)]
    public void UserResizeDisablesOnlyTheAdjustedAutomaticAxis(SizeToContent initialMode, double userWidth,
        double userHeight, SizeToContent expectedMode, double expectedWidth, double expectedHeight)
    {
        var content = new Border { Width = 320, Height = 120 };
        var window = CreateWindow(content);
        window.SizeToContent = initialMode;
        var nativeSize = new Size(460, 500);
        var requests = new List<Size>();
        using var sizing = new WindowContentSizing(window, () => { }, size =>
        {
            requests.Add(size);
            nativeSize = size;
            window.Width = size.Width;
            window.Height = size.Height;
        }, () => nativeSize);

        try
        {
            window.Show();
            PumpLayout(window);
            sizing.FitToContent();
            nativeSize = new(userWidth, userHeight);
            window.Width = userWidth;
            window.Height = userHeight;
            sizing.OnResized(nativeSize, WindowResizeReason.User);
            PumpLayout(window);

            Assert.Equal(expectedMode, sizing.RequestedMode);
            if (expectedMode == SizeToContent.Manual)
            {
                Assert.Same(content, window.Content);
            }
            else
            {
                Assert.Same(content, Assert.IsType<WindowContentSizingPresenter>(window.Content).Child);
            }

            var requestsAfterUserResize = requests.Count;
            content.Width = 360;
            content.Height = 160;
            PumpLayout(window);
            sizing.FitToContent();
            PumpLayout(window);

            Assert.Equal(new Size(expectedWidth, expectedHeight), nativeSize);
            Assert.False(sizing.NeedsResize);
            Assert.Equal(requestsAfterUserResize + (expectedMode == SizeToContent.Manual ? 0 : 1), requests.Count);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 释放后不再接管窗口内容和自适应设置，也不触发刷新或原生缩放。
    /// </summary>
    [AvaloniaFact]
    public void DisposeReleasesWindowPropertySubscription()
    {
        var window = CreateWindow(new Border { Height = 120 });
        var nativeSize = new Size(460, 500);
        var refreshCount = 0;
        var resizeCount = 0;
        var sizing = new WindowContentSizing(window, () => refreshCount++, _ => resizeCount++,
            () => nativeSize);

        try
        {
            window.Show();
            PumpLayout(window);
            sizing.Dispose();
            sizing.Dispose();
            var refreshCountAfterDispose = refreshCount;
            var replacement = new Border { Width = 280, Height = 180 };
            window.Content = replacement;
            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.MinWidth = 240;
            PumpLayout(window);
            sizing.FitToContent();

            Assert.Same(replacement, window.Content);
            Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);
            Assert.Equal(refreshCountAfterDispose, refreshCount);
            Assert.Equal(0, resizeCount);
            Assert.False(sizing.NeedsResize);
        }
        finally
        {
            sizing.Dispose();
            window.Close();
        }
    }

    /// <summary>
    /// 原生客户区读取失败会停止内容适配并交回错误边界，不从布局事件抛出。
    /// </summary>
    [AvaloniaFact]
    public void NativeReadFailureStopsSizingAndReportsErrorOnce()
    {
        var window = CreateWindow(new Border { Height = 120 });
        var error = new Win32Exception(5);
        var errors = new List<Exception>();
        var resizeCount = 0;
        using var sizing = new WindowContentSizing(window, () => { }, _ => resizeCount++,
            () => throw error, errors.Add);

        try
        {
            window.Show();
            PumpLayout(window);
            Assert.False(sizing.NeedsResize);
            sizing.FitToContent();
            window.Width = 620;
            PumpLayout(window);
            sizing.FitToContent();

            Assert.Same(error, sizing.LastError);
            Assert.Same(error, Assert.Single(errors));
            Assert.Equal(0, resizeCount);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window CreateWindow(Control content)
    {
        return new()
        {
            Width = 460,
            Height = 500,
            SizeToContent = SizeToContent.Height,
            Content = content
        };
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
