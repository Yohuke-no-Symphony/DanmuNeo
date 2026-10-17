using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Threading;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class NativeMenuExporterProbe : ITopLevelNativeMenuExporter, IDisposable
{
    private readonly AvaloniaObject owner;
    private readonly List<NativeMenu> observedMenus = [];
    private readonly List<NativeMenuItem> observedItems = [];
    private Dictionary<NativeMenuItem, NativeMenu> exportedSubmenus = [];
    private HashSet<NativeMenuItem> exportedItems = [];
    private bool pending;
    private bool disposed;

    internal NativeMenuExporterProbe(AvaloniaObject owner)
    {
        this.owner = owner;
        owner.PropertyChanged += OnOwnerPropertyChanged;

        if (owner is Window window)
        {
            window.Closed += OnWindowClosed;
        }

        if (NativeMenu.GetMenu(owner) is { } menu)
        {
            SetNativeMenu(menu);
        }
    }

    internal NativeMenu? Root { get; private set; }
    internal int AssignmentCount { get; private set; }
    internal bool IsClosed { get; private set; }
    internal bool HasPendingChanges => pending;
    internal int IconExportCount { get; private set; }
    internal int IconExportsAfterClose { get; private set; }

    /// <inheritdoc/>
    public bool IsNativeMenuExported => Root is not null;

    /// <inheritdoc/>
    public event EventHandler? OnIsNativeMenuExportedChanged
    {
        add
        {
        }
        remove
        {
        }
    }

    /// <summary>
    /// 保留 Avalonia 11.3.12 exporter 的根对象身份约束，包括 null 会创建新根的行为。
    /// </summary>
    public void SetNativeMenu(NativeMenu? menu)
    {
        ObjectDisposedException.ThrowIf(disposed || IsClosed, this);
        var effectiveMenu = menu ?? new();

        if (Root is not null && !ReferenceEquals(Root, effectiveMenu))
        {
            throw new ArgumentException("The menu being updated does not match.", nameof(menu));
        }

        Root = effectiveMenu;
        AssignmentCount++;
        pending = true;
        ExportPendingChanges();
    }

    internal void ExportPendingChanges()
    {
        if (!pending || disposed || Root is null)
        {
            return;
        }

        pending = false;
        UnsubscribeTree();
        var currentSubmenus = new Dictionary<NativeMenuItem, NativeMenu>();
        var currentItems = new HashSet<NativeMenuItem>();
        ValidateMenu(Root, currentSubmenus, currentItems, []);
        exportedSubmenus = currentSubmenus;
        exportedItems = currentItems;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        disposed = true;
        pending = false;
        owner.PropertyChanged -= OnOwnerPropertyChanged;

        if (owner is Window window)
        {
            window.Closed -= OnWindowClosed;
        }

        UnsubscribeTree();
    }

    private void ValidateMenu(NativeMenu menu, Dictionary<NativeMenuItem, NativeMenu> currentSubmenus,
        HashSet<NativeMenuItem> currentItems, HashSet<NativeMenu> visited)
    {
        if (!visited.Add(menu))
        {
            throw new InvalidOperationException("A native menu belongs to more than one branch.");
        }

        observedMenus.Add(menu);
        ((INotifyCollectionChanged)menu.Items).CollectionChanged += OnMenuItemsChanged;

        foreach (var item in menu.Items.OfType<NativeMenuItem>())
        {
            if (!ReferenceEquals(item.Parent, menu))
            {
                throw new InvalidOperationException("A native menu item belongs to another menu.");
            }

            observedItems.Add(item);
            item.PropertyChanged += OnMenuItemPropertyChanged;
            currentItems.Add(item);

            if (!exportedItems.Contains(item))
            {
                ExportIcon(item);
            }

            if (item.Menu is not { } submenu)
            {
                continue;
            }

            if (!ReferenceEquals(submenu.Parent, item))
            {
                throw new InvalidOperationException("A submenu belongs to another menu item.");
            }

            if (exportedSubmenus.TryGetValue(item, out var previous) && !ReferenceEquals(previous, submenu))
            {
                throw new ArgumentException("The menu being updated does not match.", nameof(menu));
            }

            currentSubmenus.Add(item, submenu);
            ValidateMenu(submenu, currentSubmenus, currentItems, visited);
        }
    }

    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == NativeMenu.MenuProperty)
        {
            SetNativeMenu(NativeMenu.GetMenu(owner));
        }
    }

    private void OnMenuItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        QueueChanges();
    }

    private void OnMenuItemPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == NativeMenuItem.IsEnabledProperty)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(IsClosed, this);

        if (e.Property == NativeMenuItem.IconProperty && sender is NativeMenuItem item)
        {
            ExportIcon(item);
            return;
        }

        QueueChanges();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        IsClosed = true;
    }

    private void QueueChanges()
    {
        if (pending)
        {
            return;
        }

        pending = true;
        Dispatcher.UIThread.Post(ExportPendingChanges, DispatcherPriority.Background);
    }

    private void ExportIcon(NativeMenuItem item)
    {
        if (item.Icon is not { } icon)
        {
            return;
        }

        using var stream = new MemoryStream();
        icon.Save(stream);
        IconExportCount++;

        if (IsClosed)
        {
            IconExportsAfterClose++;
        }
    }

    private void UnsubscribeTree()
    {
        foreach (var menu in observedMenus)
        {
            ((INotifyCollectionChanged)menu.Items).CollectionChanged -= OnMenuItemsChanged;
        }

        foreach (var item in observedItems)
        {
            item.PropertyChanged -= OnMenuItemPropertyChanged;
        }

        observedMenus.Clear();
        observedItems.Clear();
    }
}
