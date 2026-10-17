using Avalonia;
using Avalonia.Controls;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 融合式标题栏。
/// </summary>
public partial class TitleBarView : UserControl, IWindowTitleBar
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

    Control IWindowTitleBar.View => this;

    bool IWindowTitleBar.IsDragRegion(Point windowPoint)
    {
        return IsDragRegion(windowPoint);
    }

    internal bool IsDragRegion(Point windowPoint)
    {
        return SharedTitleBar.IsDragRegion(windowPoint);
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
