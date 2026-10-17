using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Lucide.Avalonia;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Menus;

internal sealed class WorkbenchApplicationMenu : IDisposable
{
    private readonly NativeMenu menu;
    private readonly NativeMenuItem settings;

    internal WorkbenchApplicationMenu(Application application, WorkbenchMenuCatalog catalog, WorkbenchMenuIcons icons)
    {
        menu = NativeMenu.GetMenu(application) ?? new();
        NativeMenu.SetMenu(application, menu);
        settings = new(Localization.Get("Workbench.Menu.Settings"))
        {
            Command = catalog.SettingsCommand,
            Icon = icons.GetNative(LucideIconKind.Settings),
            Gesture = new(Key.OemComma, KeyModifiers.Meta)
        };
        menu.Items.Insert(0, settings);
        Localization.LanguageChanged += OnLanguageChanged;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Localization.LanguageChanged -= OnLanguageChanged;
        menu.Items.Remove(settings);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        settings.Header = Localization.Get("Workbench.Menu.Settings");
    }
}
