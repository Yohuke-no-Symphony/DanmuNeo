using Avalonia;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>保持独立账号上下文的粉丝牌Dock面板。</summary>
public partial class AccountMedalsPanelView : WorkbenchPanelView
{
    private readonly List<Visual> observedVisuals = [];
    private bool attached;

    /// <summary>创建供XAML加载使用的面板。</summary>
    public AccountMedalsPanelView()
    {
        InitializeComponent();
    }

    /// <summary>连接工作台和应用拥有的粉丝牌视图模型。</summary>
    public AccountMedalsPanelView(WorkbenchContext context, AccountMedalsViewModel viewModel) : this()
    {
        DataContext = viewModel;
        InitializeContext(context);
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        foreach (var visual in this.GetVisualAncestors().Prepend(this))
        {
            observedVisuals.Add(visual);
            visual.PropertyChanged += OnVisibilityChanged;
        }
        UpdateActivation();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleaseActivation();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override void OnContextInitialized() => UpdateActivation();

    /// <inheritdoc/>
    protected override void DisposePanel() => ReleaseActivation();

    private void OnVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            UpdateActivation();
        }
    }

    private void UpdateActivation()
    {
        if (DataContext is not AccountMedalsViewModel viewModel)
        {
            return;
        }
        if (attached && IsEffectivelyVisible && IsEnabled)
        {
            _ = viewModel.ActivateAsync();
        }
        else
        {
            viewModel.Deactivate();
        }
    }

    private void ReleaseActivation()
    {
        attached = false;
        foreach (var visual in observedVisuals)
        {
            visual.PropertyChanged -= OnVisibilityChanged;
        }
        observedVisuals.Clear();
        if (DataContext is AccountMedalsViewModel viewModel)
        {
            viewModel.Deactivate();
        }
    }
}
