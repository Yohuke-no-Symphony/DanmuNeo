using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 融合式标题栏。
/// </summary>
public partial class TitleBarView : UserControl, IWindowTitleBar
{
    private Window? observedWindow;

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

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        observedWindow = TopLevel.GetTopLevel(this) as Window;
        Localization.LanguageChanged += OnLanguageChanged;

        if (observedWindow is not null)
        {
            observedWindow.PropertyChanged += OnWindowPropertyChanged;
        }

        RefreshPinState();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Localization.LanguageChanged -= OnLanguageChanged;

        if (observedWindow is not null)
        {
            observedWindow.PropertyChanged -= OnWindowPropertyChanged;
            observedWindow = null;
        }

        base.OnDetachedFromVisualTree(e);
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
        RefreshPinState();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowBase.TopmostProperty)
        {
            RefreshPinState();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshPinState();
    }

    private void RefreshPinState()
    {
        var isTopmost = observedWindow?.Topmost == true;
        PinButton.Classes.Set("selected", isTopmost);
        PinOutlineIcon.IsVisible = !isTopmost;
        PinSolidIcon.IsVisible = isTopmost;
        var tip = Localization.Get(isTopmost ? "UI.TitleBarView.UnpinWindow" : "UI.TitleBarView.PinWindow");
        ToolTip.SetTip(PinButton, tip);
        AutomationProperties.SetName(PinButton, tip);
    }
}
