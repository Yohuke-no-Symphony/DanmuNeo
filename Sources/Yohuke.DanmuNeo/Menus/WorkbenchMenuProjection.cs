using Avalonia.Controls;

namespace Yohuke.DanmuNeo.Menus;

internal sealed class WorkbenchMenuProjection : IDisposable
{
    private readonly WorkbenchMenuCatalog catalog;
    private readonly Menu? windowMenu;
    private readonly Dictionary<string, MenuItem> windowItems = [];
    private readonly Dictionary<string, NativeMenuItem> nativeItems = [];
    private string[] order = [];
    private bool nativeMenuVisible = true;
    private bool disposed;

    internal WorkbenchMenuProjection(WorkbenchMenuCatalog catalog, Menu? windowMenu = null)
    {
        this.catalog = catalog;
        this.windowMenu = windowMenu;
        catalog.Changed += Refresh;
        Refresh();
    }

    internal NativeMenu NativeMenu { get; } = new();

    internal void SetNativeMenuVisible(bool visible)
    {
        if (disposed)
        {
            return;
        }

        nativeMenuVisible = visible;

        foreach (var item in NativeMenu.Items.OfType<NativeMenuItem>())
        {
            item.IsVisible = visible;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        catalog.Changed -= Refresh;
    }

    private void Refresh()
    {
        if (disposed)
        {
            return;
        }

        var entries = catalog.Entries;
        var currentOrder = entries.SelectMany(group => new[] { group.Id }
            .Concat(group.Children?.Select(item => item.Id) ?? [])).ToArray();

        if (!order.SequenceEqual(currentOrder))
        {
            order = currentOrder;
            windowMenu?.Items.Clear();
            NativeMenu.Items.Clear();
            windowItems.Clear();
            nativeItems.Clear();

            foreach (var group in entries)
            {
                var windowGroup = new MenuItem { Header = group.Header };
                var nativeGroup = new NativeMenuItem(group.Header)
                {
                    Menu = new(),
                    IsVisible = nativeMenuVisible
                };
                windowMenu?.Items.Add(windowGroup);
                NativeMenu.Items.Add(nativeGroup);

                foreach (var item in group.Children ?? [])
                {
                    var toggle = item.IsCheckable ? MenuItemToggleType.CheckBox : MenuItemToggleType.None;
                    var windowItem = new MenuItem
                    {
                        Header = item.Header,
                        Command = item.Command,
                        ToggleType = toggle,
                        InputGesture = item.Gesture
                    };
                    var nativeItem = new NativeMenuItem(item.Header)
                    {
                        Command = item.Command,
                        ToggleType = item.IsCheckable ? NativeMenuItemToggleType.CheckBox : NativeMenuItemToggleType.None
                    };
                    windowGroup.Items.Add(windowItem);
                    nativeGroup.Menu.Items.Add(nativeItem);
                    windowItems.Add(item.Id, windowItem);
                    nativeItems.Add(item.Id, nativeItem);
                }
            }
        }

        foreach (var item in entries.SelectMany(group => group.Children ?? []))
        {
            windowItems[item.Id].Header = item.Header;
            windowItems[item.Id].IsChecked = item.IsChecked;
            nativeItems[item.Id].Header = item.Header;
            nativeItems[item.Id].IsChecked = item.IsChecked;
        }
    }
}
