using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Services;

internal static class AppLanguageService
{
    internal static void ApplyPreference(string languageId)
    {
        var availableLanguageId = string.Equals(languageId, "system", StringComparison.OrdinalIgnoreCase)
            ? "system"
            : Localization.KnownLanguages.FirstOrDefault(language =>
                string.Equals(language.LanguageID, languageId, StringComparison.OrdinalIgnoreCase))?.LanguageID ??
              LocalizationCatalog.DEFAULT_LANGUAGE_ID;
        Localization.SetLanguage(availableLanguageId);
    }
}
