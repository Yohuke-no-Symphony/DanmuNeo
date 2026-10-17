using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 为各类窗口提供统一的标题表面、宿主内容和窗口拖拽行为。
/// </summary>
public partial class WindowTitleBar : UserControl, IWindowTitleBar
{
    /// <summary>
    /// 平台测量窗口按钮前使用的标题栏基准高度。
    /// </summary>
    public const double DEFAULT_HEIGHT = 42;

    /// <summary>
    /// 窗口标题。
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<WindowTitleBar, string?>(nameof(Title));

    /// <summary>
    /// 由宿主提供的标题内容。
    /// </summary>
    public static readonly StyledProperty<Control?> HeaderContentProperty =
        AvaloniaProperty.Register<WindowTitleBar, Control?>(nameof(HeaderContent));

    /// <summary>
    /// 位于原生窗口按钮之前的窗口操作内容。
    /// </summary>
    public static readonly StyledProperty<Control?> RightContentProperty =
        AvaloniaProperty.Register<WindowTitleBar, Control?>(nameof(RightContent));

    /// <summary>
    /// 原生窗口按钮占用的边缘区域。
    /// </summary>
    public static readonly StyledProperty<Thickness> CaptionInsetsProperty =
        AvaloniaProperty.Register<WindowTitleBar, Thickness>(nameof(CaptionInsets));

    /// <summary>
    /// 初始化共享标题栏。
    /// </summary>
    public WindowTitleBar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 获取或设置窗口标题。
    /// </summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// 获取或设置替代默认标题的宿主内容。
    /// </summary>
    public Control? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    /// <summary>
    /// 获取或设置窗口操作内容。
    /// </summary>
    public Control? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    /// <summary>
    /// 获取或设置原生窗口按钮占用的边缘区域。
    /// </summary>
    public Thickness CaptionInsets
    {
        get => GetValue(CaptionInsetsProperty);
        set => SetValue(CaptionInsetsProperty, value);
    }

    Control IWindowTitleBar.View => this;

    bool IWindowTitleBar.IsDragRegion(Point windowPoint)
    {
        return IsDragRegion(windowPoint);
    }

    internal bool IsDragRegion(Point windowPoint)
    {
        if (TopLevel.GetTopLevel(this) is not Window window || !IsEffectivelyVisible ||
            window.TranslatePoint(windowPoint, this) is not { } point ||
            !new Rect(Bounds.Size).Contains(point) || point.X < CaptionInsets.Left ||
            point.X >= Bounds.Width - CaptionInsets.Right)
        {
            return false;
        }

        if (RightContent is { IsEffectivelyVisible: true } actions &&
            this.TranslatePoint(point, actions) is { } actionsPoint &&
            new Rect(actions.Bounds.Size).Contains(actionsPoint))
        {
            return false;
        }

        return !this.GetVisualDescendants().OfType<Control>().Any(control =>
            control.IsEffectivelyVisible && control.IsHitTestVisible &&
            (control.Focusable || control is Button or Menu or MenuItem) &&
            window.TranslatePoint(windowPoint, control) is { } controlPoint &&
            new Rect(control.Bounds.Size).Contains(controlPoint));
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !IsDragRegion(e.GetPosition(window)))
        {
            return;
        }

        if (e.ClickCount % 2 == 0)
        {
            if (window.CanResize && window.CanMaximize)
            {
                window.WindowState = window.WindowState switch
                {
                    WindowState.Normal => WindowState.Maximized,
                    WindowState.Maximized => WindowState.Normal,
                    _ => window.WindowState
                };
            }

            e.Handled = true;
            return;
        }

        window.BeginMoveDrag(e);
        e.Handled = true;
    }
}
