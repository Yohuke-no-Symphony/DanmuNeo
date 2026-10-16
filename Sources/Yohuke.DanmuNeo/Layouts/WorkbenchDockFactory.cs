using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchDockFactory : Factory
{
    private readonly Action changed;
    private readonly Action beginMigration;
    private readonly Action<IDockable?> closing;

    internal WorkbenchControlRecycling ControlRecycling { get; } = new();

    internal WorkbenchDockFactory(
        Func<IHostWindow?> createHost,
        Action changed,
        Action beginMigration,
        Action<IDockable?> closing)
    {
        this.changed = changed;
        this.beginMigration = beginMigration;
        this.closing = closing;
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>> { [nameof(IDockWindow)] = createHost };
        HideToolsOnClose = true;
    }

    /// <summary>
    /// 创建允许隐藏最后一个面板的工具标签组。
    /// </summary>
    public override IToolDock CreateToolDock()
    {
        return new WorkbenchToolDock { CanCloseLastDockable = true, CanPin = false };
    }

    /// <summary>
    /// 创建包含完整 Dock 集合的工作台根节点。
    /// </summary>
    public override IRootDock CreateRootDock()
    {
        return new WorkbenchRootDock
        {
            LeftPinnedDockables = CreateList<IDockable>(),
            RightPinnedDockables = CreateList<IDockable>(),
            TopPinnedDockables = CreateList<IDockable>(),
            BottomPinnedDockables = CreateList<IDockable>(),
            HiddenDockables = CreateList<IDockable>(),
            Windows = CreateList<IDockWindow>()
        };
    }

    /// <summary>
    /// 创建按比例分割的空间容器。
    /// </summary>
    public override IProportionalDock CreateProportionalDock()
    {
        return new WorkbenchProportionalDock();
    }

    /// <summary>
    /// 在拖动移除旧宿主前开启原生视图迁移边界。
    /// </summary>
    public override void RemoveDockable(IDockable dockable, bool collapse)
    {
        beginMigration();
        base.RemoveDockable(dockable, collapse);
        changed();
    }

    /// <summary>
    /// 同组重排前保留原生视图的迁移作用域。
    /// </summary>
    public override void MoveDockable(IDock dock, IDockable sourceDockable, IDockable targetDockable)
    {
        beginMigration();
        base.MoveDockable(dock, sourceDockable, targetDockable);
        changed();
    }

    /// <summary>
    /// 跨标签组移动前保留原生视图的迁移作用域。
    /// </summary>
    public override void MoveDockable(IDock sourceDock, IDock targetDock, IDockable sourceDockable, IDockable? targetDockable)
    {
        beginMigration();
        base.MoveDockable(sourceDock, targetDock, sourceDockable, targetDockable);
        changed();
    }

    /// <summary>
    /// 同组交换标签前保留原生视图的迁移作用域。
    /// </summary>
    public override void SwapDockable(IDock dock, IDockable sourceDockable, IDockable targetDockable)
    {
        beginMigration();
        base.SwapDockable(dock, sourceDockable, targetDockable);
        changed();
    }

    /// <summary>
    /// 跨组交换标签前保留原生视图的迁移作用域。
    /// </summary>
    public override void SwapDockable(IDock sourceDock, IDock targetDock, IDockable sourceDockable, IDockable targetDockable)
    {
        beginMigration();
        base.SwapDockable(sourceDock, targetDock, sourceDockable, targetDockable);
        changed();
    }

    /// <summary>
    /// 创建浮窗前开启原生视图迁移边界。
    /// </summary>
    public override void FloatDockable(IDockable dockable, DockWindowOptions? options)
    {
        beginMigration();
        base.FloatDockable(dockable, options);
        changed();
    }

    /// <summary>
    /// 通知业务处理显式关闭，之后由 Dock 执行隐藏。
    /// </summary>
    public override bool OnDockableClosing(IDockable? dockable)
    {
        if (!base.OnDockableClosing(dockable))
        {
            return false;
        }

        closing(dockable);
        return true;
    }

    /// <summary>
    /// 停靠后安排同步布局快照。
    /// </summary>
    public override void OnDockableDocked(IDockable? dockable, DockOperation operation)
    {
        base.OnDockableDocked(dockable, operation);
        changed();
    }

    /// <summary>
    /// 标签隐藏后安排同步布局快照。
    /// </summary>
    public override void OnDockableHidden(IDockable? dockable)
    {
        base.OnDockableHidden(dockable);
        changed();
    }

    /// <summary>
    /// 标签恢复后安排同步布局快照。
    /// </summary>
    public override void OnDockableRestored(IDockable? dockable)
    {
        base.OnDockableRestored(dockable);
        changed();
    }

    /// <summary>
    /// 窗口创建后安排同步布局快照。
    /// </summary>
    public override void OnWindowAdded(IDockWindow? window)
    {
        base.OnWindowAdded(window);
        changed();
    }

    /// <summary>
    /// 窗口移除后安排同步布局快照。
    /// </summary>
    public override void OnWindowRemoved(IDockWindow? window)
    {
        base.OnWindowRemoved(window);
        changed();
    }
}
