using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 融合式标题栏。
/// </summary>
public partial class TitleBarView : UserControl
{
    /// <summary>
    /// 标题行中的窗口菜单内容。
    /// </summary>
    public static readonly StyledProperty<Control?> MenuContentProperty =
        AvaloniaProperty.Register<TitleBarView, Control?>(nameof(MenuContent));

    /// <summary>
    /// 为原生窗口按钮保留的平台测量区域。
    /// </summary>
    public static readonly StyledProperty<Thickness> CaptionInsetsProperty =
        AvaloniaProperty.Register<TitleBarView, Thickness>(nameof(CaptionInsets));

#if DEBUG
    private DanmuStylePreviewWindow? danmuStylePreviewWindow;
#endif

    /// <summary>
    /// 初始化标题栏。
    /// </summary>
    public TitleBarView()
    {
        InitializeComponent();
#if DEBUG
        DebugToolsButton.IsVisible = true;
#endif
    }

    /// <summary>
    /// 获取或设置标题行中的窗口菜单。
    /// </summary>
    public Control? MenuContent
    {
        get => GetValue(MenuContentProperty);
        set => SetValue(MenuContentProperty, value);
    }

    /// <summary>
    /// 获取或设置原生窗口按钮占用的边缘区域。
    /// </summary>
    public Thickness CaptionInsets
    {
        get => GetValue(CaptionInsetsProperty);
        set => SetValue(CaptionInsetsProperty, value);
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

        if (MenuContent is { IsEffectivelyVisible: true } menu &&
            this.TranslatePoint(point, menu) is { } menuPoint && new Rect(menu.Bounds.Size).Contains(menuPoint))
        {
            return false;
        }

        if (ListenToggleButton.IsEffectivelyVisible &&
            this.TranslatePoint(point, ListenToggleButton) is { } listenPoint &&
            new Rect(ListenToggleButton.Bounds.Size).Contains(listenPoint))
        {
            return false;
        }

        return this.TranslatePoint(point, WindowButtons) is not { } actionsPoint ||
               !new Rect(WindowButtons.Bounds.Size).Contains(actionsPoint);
    }

    private void DebugTools_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
#if DEBUG
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (danmuStylePreviewWindow is { IsVisible: true })
        {
            danmuStylePreviewWindow.Activate();
            return;
        }

        danmuStylePreviewWindow = new();
        danmuStylePreviewWindow.Closed += (_, _) => danmuStylePreviewWindow = null;
        danmuStylePreviewWindow.Show(owner);
#endif
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
    }

    private void Pin_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        window.Topmost = !window.Topmost;
        PinButton.Classes.Set("selected", window.Topmost);
        PinOutlineIcon.IsVisible = !window.Topmost;
        PinSolidIcon.IsVisible = window.Topmost;
        ToolTip.SetTip(PinButton, window.Topmost ? "取消窗口置顶" : "窗口置顶");
    }
}
