using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Recycling;
using Avalonia.Controls.Recycling.Model;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchControlRecycling : IControlRecycling
{
    private readonly ControlRecycling recycling = new();

    public bool TryToUseIdAsKey
    {
        get => recycling.TryToUseIdAsKey;
        set => recycling.TryToUseIdAsKey = value;
    }

    /// <inheritdoc/>
    public bool TryGetValue(object? data, out object? control)
    {
        return recycling.TryGetValue(data, out control);
    }

    /// <inheritdoc/>
    public void Add(object data, object control)
    {
        recycling.Add(data, control);
    }

    /// <summary>
    /// 从模板的实际呈现器释放缓存视图，避免将模型宿主误判为无法移除的控件父级。
    /// </summary>
    public object? Build(object? data, object? existing, object? parent)
    {
        if (data is WorkbenchDockPanel { Owner: IDock owner } panel && parent is Control requestingControl)
        {
            var requestingTool = requestingControl as ToolControl
                ?? requestingControl.GetVisualAncestors().OfType<ToolControl>().FirstOrDefault();

            if (requestingTool?.DataContext is IToolDock requestingDock && !ReferenceEquals(owner, requestingDock) &&
                requestingDock.VisibleDockables?.Contains(panel) != true)
            {
                return null;
            }
        }

        var key = data;

        if (TryToUseIdAsKey && data is IControlRecyclingIdProvider provider &&
            provider.GetControlRecyclingId() is { } id && !string.IsNullOrWhiteSpace(id))
        {
            key = id;
        }

        var candidate = recycling.TryGetValue(key, out var cached)
            ? cached as Control
            : (data as WorkbenchDockPanel)?.View;

        if (candidate is not null && !ReferenceEquals(existing, candidate) &&
            candidate.Parent is ContentControl logicalHost && !ReferenceEquals(logicalHost.Content, candidate) &&
            candidate.GetVisualParent() is ContentPresenter presenter &&
            ReferenceEquals(logicalHost.Presenter, presenter) && ReferenceEquals(presenter.Child, candidate))
        {
            presenter.SetCurrentValue(ContentPresenter.ContentProperty, null);
            presenter.UpdateChild();
        }

        return recycling.Build(data, existing, parent);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        recycling.Clear();
    }
}
