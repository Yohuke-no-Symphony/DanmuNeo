using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证 窗口内容自适应使用独立于当前客户区的自然尺寸和明确约束。
/// </summary>
public sealed class WindowContentSizingPresenterTests
{
    /// <summary>
    /// 当前窗口较窄或较矮时，固定宽度和内容自然高度仍取自请求约束。
    /// </summary>
    [AvaloniaFact]
    public void FixedWidthAndNaturalHeightDoNotFollowCurrentViewport()
    {
        var content = new Border { Width = 1000, Height = 120 };
        var presenter = new WindowContentSizingPresenter(content);
        presenter.UpdateConstraints(SizeToContent.Height, new(520, 80), default, new(1200, 900), default);
        var window = new Window { Width = 240, Height = 80, Content = presenter };

        try
        {
            window.Show();
            PumpLayout(window);

            Assert.True(presenter.HasMeasured);
            Assert.Equal(520, presenter.DesiredClientSize.Width);
            Assert.Equal(120, presenter.DesiredClientSize.Height);
            Assert.Equal(520, content.DesiredSize.Width);
            Assert.True(window.ClientSize.Width < presenter.DesiredClientSize.Width);
            Assert.True(window.ClientSize.Height < presenter.DesiredClientSize.Height);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 手动客户区保持较矮时，文案变化仍能自然增长并恢复为短文案高度。
    /// </summary>
    [AvaloniaFact]
    public void DynamicWrappedTextGrowsAndShrinksBeyondCurrentHeight()
    {
        var text = new TextBlock
        {
            Text = "检查完成",
            FontSize = 14,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap
        };
        var presenter = new WindowContentSizingPresenter(text);
        presenter.UpdateConstraints(SizeToContent.Height, new(480, 60), default, new(800, 900), default);
        var window = new Window { Width = 480, Height = 60, Content = presenter };

        try
        {
            window.Show();
            PumpLayout(window);
            var shortHeight = presenter.DesiredClientSize.Height;
            Assert.True(shortHeight > 0);

            text.Text = string.Concat(Enumerable.Repeat("更新状态发生变化，需要完整显示新的说明。", 30));
            PumpLayout(window);
            Assert.True(presenter.DesiredClientSize.Height > shortHeight + 100);
            Assert.True(presenter.DesiredClientSize.Height > window.ClientSize.Height);

            text.Text = "检查完成";
            PumpLayout(window);
            Assert.Equal(shortHeight, presenter.DesiredClientSize.Height);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 超出屏幕高度的内容遵循有限最大高度，滚动区域保留完整内容范围。
    /// </summary>
    [AvaloniaFact]
    public void MaximumHeightKeepsOversizedContentScrollable()
    {
        var content = new Border { Width = 100, Height = 1600 };
        var scroll = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var presenter = new WindowContentSizingPresenter(scroll);
        presenter.UpdateConstraints(SizeToContent.Height, new(400, 240), default, new(800, 240), default);
        var window = new Window { Width = 400, Height = 240, Content = presenter };

        try
        {
            window.Show();
            PumpLayout(window);

            Assert.Equal(240, presenter.DesiredClientSize.Height);
            Assert.True(scroll.Viewport.Height > 0);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);

            content.Height = 72;
            PumpLayout(window);
            Assert.Equal(72, presenter.DesiredClientSize.Height);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 外部客户区内边距和边框只加入目标客户区一次，不重复加入内容期望尺寸。
    /// </summary>
    [AvaloniaFact]
    public void ExternalPaddingAndBorderAreCountedOnce()
    {
        var content = new Border { Width = 160, Height = 90 };
        var presenter = new WindowContentSizingPresenter(content);
        var container = new Border
        {
            Padding = new(5, 7, 11, 13),
            BorderThickness = new(2, 3, 4, 5),
            Child = presenter
        };
        var insets = container.Padding + container.BorderThickness;
        presenter.UpdateConstraints(SizeToContent.Height, new(400, 300), default, new(800, 900), insets);
        var window = new Window { Width = 400, Height = 300, Content = container };

        try
        {
            window.Show();
            PumpLayout(window);

            Assert.Equal(new Size(400, 118), presenter.DesiredClientSize);
            Assert.Equal(90, presenter.DesiredSize.Height);
            Assert.Equal(118, container.DesiredSize.Height);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 分数内容尺寸及相同约束不会随布局轮次累积误差或重复失效测量。
    /// </summary>
    [AvaloniaFact]
    public void FractionalSizesRemainStableAcrossLayoutPasses()
    {
        var content = new Border { Width = 231.625, Height = 73.375, UseLayoutRounding = false };
        var presenter = new WindowContentSizingPresenter(content) { UseLayoutRounding = false };
        var fixedClientSize = new Size(402.25, 100);
        var maxClientSize = new Size(1000, 1000);
        var insets = new Thickness(0.125, 0.375, 0.625, 0.875);
        presenter.UpdateConstraints(SizeToContent.Height, fixedClientSize, default, maxClientSize, insets);
        var window = new Window { Width = 300, Height = 100, UseLayoutRounding = false, Content = presenter };

        try
        {
            window.Show();
            PumpLayout(window);

            for (var index = 0; index < 20; index++)
            {
                Assert.Equal(new Size(402.25, 74.625), presenter.DesiredClientSize);
                Assert.True(presenter.IsMeasureValid);
                presenter.UpdateConstraints(SizeToContent.Height, fixedClientSize, default, maxClientSize, insets);
                Assert.True(presenter.IsMeasureValid);
                PumpLayout(window);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 自动宽度遵守上限，未启用自动调整的高度仍使用明确请求值。
    /// </summary>
    [AvaloniaFact]
    public void AutomaticWidthUsesMaximumWhileHeightKeepsRequestedSize()
    {
        var content = new Border { Width = 1000, Height = 150 };
        var presenter = new WindowContentSizingPresenter(content);
        presenter.UpdateConstraints(SizeToContent.Width, new(200, 180), default, new(360, 600), default);
        var window = new Window { Width = 200, Height = 80, Content = presenter };

        try
        {
            window.Show();
            PumpLayout(window);

            Assert.Equal(new Size(360, 180), presenter.DesiredClientSize);
            Assert.Equal(360, content.DesiredSize.Width);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 双轴自动尺寸使用自然内容，并在约束改变后重新测量最小和最大尺寸。
    /// </summary>
    [AvaloniaFact]
    public void BothAutomaticAxesRecomputeAfterConstraintsChange()
    {
        var content = new Border { Width = 280, Height = 140 };
        var presenter = new WindowContentSizingPresenter(content);
        presenter.UpdateConstraints(SizeToContent.WidthAndHeight, new(200, 80), default,
            new(double.PositiveInfinity, double.PositiveInfinity), default);
        var window = new Window { Width = 200, Height = 80, Content = presenter };

        try
        {
            window.Show();
            PumpLayout(window);
            Assert.Equal(new Size(280, 140), presenter.DesiredClientSize);

            presenter.UpdateConstraints(SizeToContent.WidthAndHeight, new(200, 80), new(320, 180),
                new(400, 300), default);
            Assert.False(presenter.HasMeasured);
            PumpLayout(window);
            Assert.Equal(new Size(320, 180), presenter.DesiredClientSize);

            presenter.UpdateConstraints(SizeToContent.WidthAndHeight, new(200, 80), default,
                new(220, 100), default);
            PumpLayout(window);
            Assert.Equal(new Size(220, 100), presenter.DesiredClientSize);
        }
        finally
        {
            window.Close();
        }
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
