using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Tests;

internal static class LocalizationTestStartup
{
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "The test host initializes process-level localization before constructing view models.")]
    internal static void Initialize()
    {
        Localization.Initialize(Path.Combine(AppContext.BaseDirectory, "i18n"));
        Localization.SetLanguage("zh-CN");
    }
}
