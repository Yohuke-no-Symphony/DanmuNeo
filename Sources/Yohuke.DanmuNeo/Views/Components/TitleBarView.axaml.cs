using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

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
        if (e.Source is Control source &&
            (source is Button or Menu or MenuItem || source.FindAncestorOfType<Button>() is not null ||
             source.FindAncestorOfType<Menu>() is not null || source.FindAncestorOfType<MenuItem>() is not null))
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is Window window &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            window.BeginMoveDrag(e);
        }
    }

    private void Minimize_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.WindowState = WindowState.Minimized;
        }
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

    private void Maximize_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.WindowState =
                window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void Close_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Close();
        }
    }
}
