using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Yohuke.DanmuNeo.Menus;

internal sealed class WorkbenchApplicationMenu : IDisposable
{
    private readonly NativeMenu menu;
    private readonly NativeMenuItem settings;

    internal WorkbenchApplicationMenu(Application application, WorkbenchMenuCatalog catalog)
    {
        menu = NativeMenu.GetMenu(application) ?? new();
        NativeMenu.SetMenu(application, menu);
        settings = new("设置…")
        {
            Command = catalog.SettingsCommand,
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
