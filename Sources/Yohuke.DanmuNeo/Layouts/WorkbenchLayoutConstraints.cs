using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Controls;
using Dock.Model.Core;
using DockOrientation = Dock.Model.Core.Orientation;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchLayoutConstraints : IDisposable
{
    private readonly Control[] views;
    private readonly HashSet<DockControl> hosts = [];
    private bool updating;

    internal WorkbenchLayoutConstraints(IEnumerable<WorkbenchDockPanel> panels)
    {
        views = panels.Select(panel => panel.View).ToArray();

        foreach (var view in views)
        {
            view.PropertyChanged += OnViewPropertyChanged;
        }
    }

    internal void Attach(DockControl host)
    {
        if (hosts.Add(host))
        {
            host.LayoutUpdated += OnLayoutUpdated;
        }
    }

    internal void Detach(DockControl host)
    {
        if (hosts.Remove(host))
        {
            host.LayoutUpdated -= OnLayoutUpdated;
        }
    }

    /// <summary>
    /// 释放永久业务视图与各个 Dock 宿主的布局观察。
    /// </summary>
    public void Dispose()
    {
        foreach (var view in views)
        {
            view.PropertyChanged -= OnViewPropertyChanged;
        }

        foreach (var host in hosts.ToArray())
        {
            Detach(host);
        }
    }

    private void OnViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Layoutable.MinHeightProperty || e.Property == Layoutable.MarginProperty)
        {
            Update();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        Update();
    }

    private void Update()
    {
        if (updating)
        {
            return;
        }

        updating = true;

        try
        {
            foreach (var host in hosts)
            {
                if (host.Layout is not { } root)
                {
                    continue;
                }

                var chromeHeights = new Dictionary<IToolDock, double>();
                var splitterHeights = new Dictionary<IDockable, double>();

                foreach (var control in host.GetVisualDescendants().OfType<ToolDockControl>())
                {
                    if (control.DataContext is not IToolDock dock)
                    {
                        continue;
                    }

                    var tool = control.GetVisualDescendants().OfType<WorkbenchToolControl>().FirstOrDefault();
                    var content = tool?.GetVisualDescendants().OfType<DeferredContentControl>()
                        .FirstOrDefault(candidate => candidate.Name == "PART_ContentPresenter");

                    if (content is not null)
                    {
                        chromeHeights[dock] = Math.Max(0, control.Bounds.Height - content.Bounds.Height);
                    }
                }

                foreach (var stack in host.GetVisualDescendants().OfType<WorkbenchProportionalStackPanel>())
                {
                    foreach (var child in stack.Children)
                    {
                        if (child.DataContext is ISplitter splitter && WorkbenchProportionalStackPanel.GetSplitter(child) is { } control)
                        {
                            splitterHeights[splitter] = double.IsFinite(control.Thickness) && control.Thickness > 0
                                ? control.Thickness
                                : 0;
                        }
                    }
                }

                var minimum = UpdateMinimum(root, chromeHeights, splitterHeights);

                if (TopLevel.GetTopLevel(host) is WorkbenchFloatingHostWindow window)
                {
                    window.MinHeight = Math.Max(WorkbenchFloatingHostWindow.DEFAULT_MIN_HEIGHT,
                        minimum + window.TitleBar.Height);
                }
            }
        }
        finally
        {
            updating = false;
        }
    }

    private static double UpdateMinimum(
        IDockable node,
        IReadOnlyDictionary<IToolDock, double> chromeHeights,
        IReadOnlyDictionary<IDockable, double> splitterHeights)
    {
        if (node is WorkbenchDockPanel panel)
        {
            return panel.View.MinHeight + panel.View.Margin.Top + panel.View.Margin.Bottom;
        }

        if (node is not IDock dock)
        {
            return 0;
        }

        var children = (dock.VisibleDockables ?? []).Where(child => child is not ISplitter).ToArray();
        var minimums = children.Select(child => UpdateMinimum(child, chromeHeights, splitterHeights)).ToArray();
        var minimum = minimums.DefaultIfEmpty().Max();

        if (dock is IToolDock tool && children.Length > 0)
        {
            minimum += chromeHeights.GetValueOrDefault(tool);
        }
        else if (dock is IProportionalDock { Orientation: DockOrientation.Vertical })
        {
            minimum = minimums.Sum() + (dock.VisibleDockables ?? []).OfType<ISplitter>()
                .Sum(splitter => splitterHeights.GetValueOrDefault(splitter));
        }

        minimum = Math.Ceiling(minimum);

        if (!double.IsFinite(dock.MinHeight) || Math.Abs(dock.MinHeight - minimum) > 0.5)
        {
            dock.MinHeight = minimum;
        }

        return minimum;
    }
}
