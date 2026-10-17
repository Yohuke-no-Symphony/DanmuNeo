using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Yohuke.DanmuNeo.I18n;
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
        Localization.LanguageChanged += OnLanguageChanged;
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
        Localization.LanguageChanged -= OnLanguageChanged;
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
        var tip = Localization.Get(independentlyTopmost
            ? "UI.FloatingWindowPinButton.UnpinIndependently"
            : window.Topmost
                ? "UI.FloatingWindowPinButton.PinWhileFollowingOwner"
                : "UI.FloatingWindowPinButton.PinIndependently");
        ToolTip.SetTip(WindowTopmostButton, tip);
        AutomationProperties.SetName(WindowTopmostButton, tip);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshState();
    }
}
