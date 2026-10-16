using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class NativeMenuExporterProbe : ITopLevelNativeMenuExporter, IDisposable
{
    private readonly AvaloniaObject owner;
    private readonly List<NativeMenu> observedMenus = [];
    private readonly List<NativeMenuItem> observedItems = [];
    private Dictionary<NativeMenuItem, NativeMenu> exportedSubmenus = [];
    private bool pending;

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
        ObjectDisposedException.ThrowIf(IsClosed, this);
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
        if (!pending || IsClosed || Root is null)
        {
            return;
        }

        pending = false;
        UnsubscribeTree();
        var currentSubmenus = new Dictionary<NativeMenuItem, NativeMenu>();
        ValidateMenu(Root, currentSubmenus, []);
        exportedSubmenus = currentSubmenus;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        owner.PropertyChanged -= OnOwnerPropertyChanged;

        if (owner is Window window)
        {
            window.Closed -= OnWindowClosed;
        }

        UnsubscribeTree();
    }

    private void ValidateMenu(NativeMenu menu, Dictionary<NativeMenuItem, NativeMenu> currentSubmenus,
        HashSet<NativeMenu> visited)
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
            ValidateMenu(submenu, currentSubmenus, visited);
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
        pending = true;
    }

    private void OnMenuItemPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == NativeMenuItem.IsEnabledProperty)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(IsClosed, this);
        pending = true;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ExportPendingChanges();
        IsClosed = true;
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
