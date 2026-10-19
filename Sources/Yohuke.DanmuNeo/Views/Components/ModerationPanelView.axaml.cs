using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>显式账号的应用级禁言面板；卸载仅暂停读取，管理状态由应用持有。</summary>
public sealed partial class ModerationPanelView : WorkbenchPanelView
{
    private readonly List<Control> observedAncestors = [];
    private bool activated;
    private bool released;

    /// <summary>创建无业务上下文的设计时面板。</summary>
    public ModerationPanelView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        LayoutUpdated += OnLayoutUpdated;
        PropertyChanged += OnVisibilityChanged;
    }

    /// <summary>连接共享管理器及工作台，不依赖当前房间。</summary>
    public ModerationPanelView(WorkbenchContext context, ModerationManagerViewModel manager) : this()
    {
        Manager = manager;
        DataContext = manager;
        InitializeContext(context);
    }

    /// <summary>应用持有的共享管理器，供行内命令显式绑定。</summary>
    public ModerationManagerViewModel Manager { get; private set; } = null!;

    /// <inheritdoc/>
    protected override void OnContextInitialized()
    {
        RefreshActivation();
    }

    /// <inheritdoc/>
    protected override void OnContextReleased()
    {
        released = true;
        activated = false;
        Manager?.Deactivate();
    }

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
        ClearAncestors();
        AttachedToVisualTree -= OnAttached;
        DetachedFromVisualTree -= OnDetached;
        LayoutUpdated -= OnLayoutUpdated;
        PropertyChanged -= OnVisibilityChanged;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ClearAncestors();

        foreach (var ancestor in this.GetVisualAncestors().OfType<Control>())
        {
            observedAncestors.Add(ancestor);
            ancestor.PropertyChanged += OnVisibilityChanged;
        }

        RefreshActivation();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ClearAncestors();
        activated = false;
        Manager?.Deactivate();
    }

    private void ClearAncestors()
    {
        foreach (var ancestor in observedAncestors)
        {
            ancestor.PropertyChanged -= OnVisibilityChanged;
        }

        observedAncestors.Clear();
    }

    private void OnVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == Window.WindowStateProperty)
        {
            RefreshActivation();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        RefreshActivation();
    }

    private void RefreshActivation()
    {
        if (released || Manager is null)
        {
            return;
        }

        var visible = this.IsAttachedToVisualTree() && IsEffectivelyVisible &&
                      TopLevel.GetTopLevel(this) is Window { IsVisible: true, WindowState: not WindowState.Minimized };

        if (activated == visible)
        {
            return;
        }

        activated = visible;

        if (visible)
        {
            _ = Manager.ActivateAsync();
        }
        else
        {
            Manager.Deactivate();
        }
    }

    private async void Search_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !WorkbenchTextInput.IsComposing(sender) && Manager.SearchUsersCommand.CanExecute(null))
        {
            e.Handled = true;
            await Manager.SearchUsersAsync();
        }
    }
}
