using System.Globalization;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class LocalizationTestScope : IDisposable
{
    private readonly string languageId = Localization.SelectedLanguageID;
    private readonly CultureInfo culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo uiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    /// <inheritdoc/>
    public void Dispose()
    {
        Localization.SetLanguage(languageId);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
    }
}
