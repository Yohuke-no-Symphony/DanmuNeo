using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Lucide.Avalonia;

namespace Yohuke.DanmuNeo.Menus;

internal sealed class WorkbenchApplicationMenu : IDisposable
{
    private readonly NativeMenu menu;
    private readonly NativeMenuItem settings;

    internal WorkbenchApplicationMenu(Application application, WorkbenchMenuCatalog catalog, WorkbenchMenuIcons icons)
    {
        menu = NativeMenu.GetMenu(application) ?? new();
        NativeMenu.SetMenu(application, menu);
        settings = new("设置…")
        {
            Command = catalog.SettingsCommand,
            Icon = icons.GetNative(LucideIconKind.Settings),
            Gesture = new(Key.OemComma, KeyModifiers.Meta)
        };
        menu.Items.Insert(0, settings);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        menu.Items.Remove(settings);
    }
}
