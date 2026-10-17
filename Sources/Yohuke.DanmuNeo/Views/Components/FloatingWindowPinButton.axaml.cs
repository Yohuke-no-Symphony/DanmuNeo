using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 浮动子窗口标题栏中的独立置顶按钮。
/// </summary>
public partial class FloatingWindowPinButton : UserControl
{
    private Window? window;

    /// <summary>
    /// 初始化浮窗置顶按钮。
    /// </summary>
    public FloatingWindowPinButton()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        window = TopLevel.GetTopLevel(this) as WorkbenchFloatingHostWindow;
        IsVisible = window is not null;

        if (window is not null)
        {
            window.PropertyChanged += OnWindowPropertyChanged;
            RefreshState();
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (window is not null)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
            window = null;
        }

        IsVisible = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void Pin_OnClick(object? sender, RoutedEventArgs e)
    {
        if (window is not null)
        {
            WorkbenchWindowTopmost.SetIsIndependentlyTopmost(window,
                !WorkbenchWindowTopmost.GetIsIndependentlyTopmost(window));
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == WorkbenchWindowTopmost.IsIndependentlyTopmostProperty ||
            change.Property == WindowBase.TopmostProperty)
        {
            RefreshState();
        }
    }

    private void RefreshState()
    {
        if (window is null)
        {
            return;
        }

        var independentlyTopmost = WorkbenchWindowTopmost.GetIsIndependentlyTopmost(window);
        WindowTopmostButton.Classes.Set("selected", independentlyTopmost);
        PinOutlineIcon.IsVisible = !independentlyTopmost;
        PinSolidIcon.IsVisible = independentlyTopmost;
        ToolTip.SetTip(WindowTopmostButton, independentlyTopmost
            ? "取消此窗口独立置顶"
            : window.Topmost
                ? "独立置顶此窗口（当前跟随主窗口置顶）"
                : "独立置顶此窗口");
    }
}
