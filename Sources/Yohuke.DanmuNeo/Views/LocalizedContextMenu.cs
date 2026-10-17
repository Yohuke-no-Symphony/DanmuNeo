using Avalonia.Controls;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Views;

internal sealed class LocalizedContextMenu : ContextMenu
{
    private readonly Dictionary<MenuItem, string> headerKeys = new();
    private bool isObservingLanguage;

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(ContextMenu);

    internal LocalizedContextMenu()
    {
        Opened += (_, _) =>
        {
            if (!isObservingLanguage)
            {
                Localization.LanguageChanged += OnLanguageChanged;
                isObservingLanguage = true;
            }

            RefreshHeaders();
        };
        Closed += (_, _) =>
        {
            Localization.LanguageChanged -= OnLanguageChanged;
            isObservingLanguage = false;
        };
    }

    internal void SetHeader(MenuItem item, string key)
    {
        headerKeys[item] = key;
        item.Header = Localization.Get(key);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshHeaders();
    }

    private void RefreshHeaders()
    {
        foreach (var (item, key) in headerKeys)
        {
            item.Header = Localization.Get(key);
        }
    }
}
