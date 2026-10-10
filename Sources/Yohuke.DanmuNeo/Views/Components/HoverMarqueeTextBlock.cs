using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 鼠标悬停时滚动溢出单行文本的控件。
/// </summary>
public sealed class HoverMarqueeTextBlock : UserControl
{
    private const double SCROLL_SPEED = 60;
    private static readonly TimeSpan START_DELAY = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan TIMER_INTERVAL = TimeSpan.FromMilliseconds(16);
    private readonly HoverMarqueeController controller = new(START_DELAY, SCROLL_SPEED);
    private readonly Stopwatch stopwatch = new();
    private readonly ScrollViewer scrollViewer;
    private readonly DispatcherTimer timer = new()
    {
        Interval = TIMER_INTERVAL
    };

    /// <summary>
    /// 显示文本。
    /// </summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HoverMarqueeTextBlock, string?>(nameof(Text));

    /// <summary>
    /// 初始化悬停滚动文本控件。
    /// </summary>
    public HoverMarqueeTextBlock()
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        textBlock.Bind(TextBlock.TextProperty, new Binding
        {
            Path = nameof(Text),
            Source = this
        });

        scrollViewer = new()
        {
            Content = textBlock,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsHitTestVisible = false
        };

        Content = scrollViewer;
        Background = Brushes.Transparent;
        ClipToBounds = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;

        timer.Tick += Timer_OnTick;
        PointerEntered += Control_OnPointerEntered;
        PointerExited += Control_OnPointerExited;
        DetachedFromVisualTree += (_, _) => StopScrolling();
        scrollViewer.PropertyChanged += ScrollViewer_OnPropertyChanged;
    }

    /// <summary>
    /// 显示文本。
    /// </summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void Control_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        controller.Reset();
        UpdateViewport();
        ApplyOffset();

        if (!controller.CanScroll)
        {
            return;
        }

        stopwatch.Restart();
        timer.Start();
    }

    private void Control_OnPointerExited(object? sender, PointerEventArgs e)
    {
        StopScrolling();
    }

    private void Timer_OnTick(object? sender, EventArgs e)
    {
        var elapsed = stopwatch.Elapsed;
        stopwatch.Restart();
        UpdateViewport();
        controller.Advance(elapsed);
        ApplyOffset();

        if (controller.IsComplete)
        {
            timer.Stop();
            stopwatch.Stop();
        }
    }

    private void ScrollViewer_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.ExtentProperty && e.Property != ScrollViewer.ViewportProperty)
        {
            return;
        }

        UpdateViewport();
        ApplyOffset();

        if (!IsPointerOver || !controller.CanScroll || controller.IsComplete || timer.IsEnabled)
        {
            return;
        }

        stopwatch.Restart();
        timer.Start();
    }

    private void UpdateViewport()
    {
        controller.UpdateViewport(scrollViewer.Extent.Width, scrollViewer.Viewport.Width);
    }

    private void ApplyOffset()
    {
        scrollViewer.Offset = new(controller.Offset, 0);
    }

    private void StopScrolling()
    {
        timer.Stop();
        stopwatch.Reset();
        controller.Reset();
        ApplyOffset();
    }
}
