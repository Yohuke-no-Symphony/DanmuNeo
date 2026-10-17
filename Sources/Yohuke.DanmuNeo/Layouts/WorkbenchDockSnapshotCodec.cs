using Dock.Model.Controls;
using Dock.Model.Core;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchDockSnapshotCodec
{
    private readonly WorkbenchDockFactory factory;
    private readonly IReadOnlyDictionary<string, WorkbenchDockPanel> panels;

    internal WorkbenchDockSnapshotCodec(WorkbenchDockFactory factory, IReadOnlyDictionary<string, WorkbenchDockPanel> panels)
    {
        this.factory = factory;
        this.panels = panels;
    }

    internal IRootDock BuildRoot(WorkspaceLayoutNode? node)
    {
        var root = factory.CreateRootDock();
        root.Id = "workspace-root-" + Guid.NewGuid().ToString("N");
        root.IsCollapsable = false;
        root.VisibleDockables = node is null ? factory.CreateList<IDockable>() : factory.CreateList(Build(node));
        root.ActiveDockable = root.VisibleDockables.FirstOrDefault();
        return root;
    }

    internal WorkspaceLayoutNode? CaptureRoot(IRootDock root)
    {
        return Capture(root);
    }

    internal static IEnumerable<IDockable> Enumerate(IDockable dockable)
    {
        yield return dockable;

        if (dockable is IDock dock)
        {
            foreach (var child in dock.VisibleDockables ?? [])
            {
                foreach (var descendant in Enumerate(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private IDockable Build(WorkspaceLayoutNode node)
    {
        IDock result;

        if (node.Kind == WorkspaceLayoutNodeKind.Tabs)
        {
            var tabs = factory.CreateToolDock();
            tabs.Id = "workspace-tabs-" + Guid.NewGuid().ToString("N");
            tabs.IsCollapsable = true;
            tabs.VisibleDockables = factory.CreateList<IDockable>(node.PanelIds.Select(id => panels[id]).Cast<IDockable>().ToArray());
            tabs.ActiveDockable = tabs.VisibleDockables.FirstOrDefault(panel => panel.Id == node.ActivePanelId)
                ?? tabs.VisibleDockables.FirstOrDefault();
            result = tabs;
        }
        else
        {
            var split = factory.CreateProportionalDock();
            split.Id = "workspace-split-" + Guid.NewGuid().ToString("N");
            split.Orientation = node.Orientation == WorkspaceLayoutOrientation.Vertical ? Orientation.Vertical : Orientation.Horizontal;
            split.VisibleDockables = factory.CreateList<IDockable>();

            foreach (var child in node.Children)
            {
                if (split.VisibleDockables.Count > 0)
                {
                    var splitter = factory.CreateProportionalDockSplitter();
                    splitter.CanResize = true;
                    splitter.ResizePreview = true;
                    split.VisibleDockables.Add(splitter);
                }

                split.VisibleDockables.Add(Build(child));
            }

            result = split;
        }

        result.Proportion = node.Proportion;
        return result;
    }

    private WorkspaceLayoutNode? Capture(IDockable dockable)
    {
        var proportion = double.IsFinite(dockable.Proportion) && dockable.Proportion > 0 ? dockable.Proportion : 1;

        if (dockable is WorkbenchDockPanel panel)
        {
            return new() { PanelIds = [panel.Id], ActivePanelId = panel.Id, Proportion = proportion };
        }

        if (dockable is not IDock dock)
        {
            throw LocalizedExceptionText.Create("Layout.UnsupportedDockNode",
                message => new InvalidDataException(message), dockable.GetType().Name);
        }

        if (dock is IToolDock)
        {
            var panelIds = (dock.VisibleDockables ?? []).OfType<WorkbenchDockPanel>().Select(value => value.Id).ToList();

            if (panelIds.Count == 0)
            {
                return null;
            }

            return new()
            {
                Proportion = proportion,
                PanelIds = panelIds,
                ActivePanelId = dock.ActiveDockable is WorkbenchDockPanel active && panelIds.Contains(active.Id)
                    ? active.Id
                    : panelIds[0]
            };
        }

        var children = (dock.VisibleDockables ?? []).Where(child => child is not ISplitter)
            .Select(Capture).OfType<WorkspaceLayoutNode>().ToList();

        if (children.Count == 0)
        {
            return null;
        }

        if (children.Count == 1)
        {
            return dock is IRootDock ? children[0] : children[0] with { Proportion = proportion };
        }

        return new()
        {
            Kind = WorkspaceLayoutNodeKind.Split,
            Orientation = dock is IProportionalDock { Orientation: Orientation.Vertical }
                ? WorkspaceLayoutOrientation.Vertical
                : WorkspaceLayoutOrientation.Horizontal,
            Proportion = proportion,
            Children = children
        };
    }
}
