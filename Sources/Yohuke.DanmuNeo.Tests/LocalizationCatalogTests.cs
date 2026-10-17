using System.Globalization;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证外部语言包发现、校验、缺项回退和系统语言解析。
/// </summary>
public sealed class LocalizationCatalogTests
{
    /// <summary>
    /// 使用包内元数据发现新增语言，并且只扫描目录根部的 JSON 文件。
    /// </summary>
    [Fact]
    public void DiscoversLanguageFromMetadataWithoutScanningNestedOrUnrelatedFiles()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("ja-JP", "日本語", fileName: "custom.json");
        var nestedDirectory = Path.Combine(directory.Path, "nested");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(Path.Combine(nestedDirectory, "fr-FR.json"), """
            {"LanguageName":"Français","LanguageID":"fr-FR","Strings":{"Greeting":"Bonjour"}}
            """);
        directory.WriteRaw("de-DE.txt", """
            {"LanguageName":"Deutsch","LanguageID":"de-DE","Strings":{"Greeting":"Hallo"}}
            """);

        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Equal(2, catalog.KnownLanguages.Count);
        Assert.Contains(catalog.KnownLanguages,
            language => language.LanguageID == "ja-JP" && language.LanguageName == "日本語");
        Assert.Equal("日本語", catalog.Get("ja-JP", "Greeting"));
        Assert.Empty(catalog.Diagnostics);
    }

    /// <summary>
    /// 语言标识不区分大小写，翻译键保留大小写语义。
    /// </summary>
    [Fact]
    public void NormalizesLanguageIdentifiersAndPreservesCaseSensitiveKeys()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("ZH-cn", "简体中文", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Greeting"] = "你好",
            ["greeting"] = "小写"
        });

        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Contains(catalog.KnownLanguages, language => language.LanguageID == "zh-CN");
        Assert.Equal("zh-CN", catalog.ResolveLanguageID("zH-cN", CultureInfo.InvariantCulture));
        Assert.Equal("你好", catalog.Get("ZH-CN", "Greeting"));
        Assert.Equal("小写", catalog.Get("zh-cn", "greeting"));
        Assert.Equal("GREETING", catalog.Get("zh-CN", "GREETING"));
    }

    /// <summary>
    /// 缺少翻译时依次使用英文默认包和原始键。
    /// </summary>
    [Fact]
    public void ResolvesSelectedLanguageThenEnglishThenUnchangedKey()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("zh-CN", "简体中文");

        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Equal("简体中文", catalog.Get("zh-CN", "Greeting"));
        Assert.Equal("Default text", catalog.Get("zh-CN", "Fallback"));
        Assert.Equal("Missing.Key", catalog.Get("zh-CN", "Missing.Key"));
    }

    /// <summary>
    /// 损坏或结构非法的可选包被排除，并提供对应文件的诊断。
    /// </summary>
    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"LanguageID\":\"ja-JP\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\"}")]
    [InlineData("{\"LanguageName\":\"  \",\"LanguageID\":\"ja-JP\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"!!!\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"System\",\"LanguageID\":\"system\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":12,\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":12,\"LanguageID\":\"ja-JP\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":[]}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":{\"Greeting\":12}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":{\"Greeting\":null}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":{\" \":\"Empty key\"}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageName\":\"重複\",\"LanguageID\":\"ja-JP\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":{},\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"日本語\",\"LanguageID\":\"ja-JP\",\"Strings\":{\"Greeting\":\"一\",\"Greeting\":\"二\"}}")]
    public void ExcludesInvalidOptionalPackagesWithFileDiagnostic(string json)
    {
        using var directory = new TemporaryLocalizationDirectory();
        var damagedPath = directory.WriteRaw("optional.json", json);

        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Single(catalog.KnownLanguages);
        var diagnostic = Assert.Single(catalog.Diagnostics);
        Assert.Equal(damagedPath, diagnostic.FilePath);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
        Assert.Equal("Hello", catalog.Get("en-US", "Greeting"));
    }

    /// <summary>
    /// 重复语言标识的全部冲突包被排除，其他语言仍可加载。
    /// </summary>
    [Fact]
    public void ExcludesEveryOptionalPackageWithDuplicateLanguageIdentifier()
    {
        using var directory = new TemporaryLocalizationDirectory();
        var firstPath = directory.WriteLanguage("ja-JP", "日本語", fileName: "first.json");
        var secondPath = directory.WriteLanguage("JA-jp", "Japanese", fileName: "second.json");
        directory.WriteLanguage("fr-FR", "Français");

        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Equal(2, catalog.KnownLanguages.Count);
        Assert.DoesNotContain(catalog.KnownLanguages, language => language.LanguageID == "ja-JP");
        Assert.Equal(2, catalog.Diagnostics.Count);
        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.FilePath == firstPath);
        Assert.Contains(catalog.Diagnostics, diagnostic => diagnostic.FilePath == secondPath);
        Assert.Equal("Français", catalog.Get("fr-FR", "Greeting"));
        Assert.Throws<ArgumentException>(() => catalog.ResolveLanguageID("ja-JP", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 缺少英文默认包时加载失败。
    /// </summary>
    [Fact]
    public void MissingEnglishDefaultLanguageFailsLoad()
    {
        using var directory = new TemporaryLocalizationDirectory(includeDefaultLanguage: false);
        directory.WriteLanguage("zh-CN", "简体中文");

        Assert.Throws<InvalidDataException>(() => LocalizationCatalog.Load(directory.Path));
    }

    /// <summary>
    /// 英文包损坏时不得使用可选语言掩盖加载失败。
    /// </summary>
    [Theory]
    [InlineData("{")]
    [InlineData("{\"LanguageName\":\"  \",\"LanguageID\":\"en-US\",\"Strings\":{}}")]
    [InlineData("{\"LanguageName\":\"English\",\"LanguageID\":\"en-US\",\"Strings\":{\"Greeting\":false}}")]
    [InlineData("{\"LanguageName\":\"English\",\"LanguageID\":\"en-US\",\"Strings\":{\"Greeting\":\"One\",\"Greeting\":\"Two\"}}")]
    public void InvalidEnglishDefaultLanguageFailsLoad(string json)
    {
        using var directory = new TemporaryLocalizationDirectory(includeDefaultLanguage: false);
        directory.WriteRaw("en-US.json", json);
        directory.WriteLanguage("zh-CN", "简体中文");

        Assert.Throws<InvalidDataException>(() => LocalizationCatalog.Load(directory.Path));
    }

    /// <summary>
    /// 英文包标识重复时加载失败，与包文件名无关。
    /// </summary>
    [Fact]
    public void DuplicateEnglishDefaultLanguageFailsLoad()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("EN-us", "Duplicate English", fileName: "duplicate.json");

        Assert.Throws<InvalidDataException>(() => LocalizationCatalog.Load(directory.Path));
    }

    /// <summary>
    /// 未安装或空白的显式语言标识不能静默回退。
    /// </summary>
    [Theory]
    [InlineData("ja-JP")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsUnknownOrEmptyExplicitLanguage(string languageID)
    {
        using var directory = new TemporaryLocalizationDirectory();
        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.ThrowsAny<ArgumentException>(() => catalog.ResolveLanguageID(languageID, CultureInfo.InvariantCulture));
        Assert.ThrowsAny<ArgumentException>(() => catalog.Get(languageID, "Greeting"));
        Assert.Equal("Hello", catalog.Get("en-US", "Greeting"));
    }

    /// <summary>
    /// 系统语言依次匹配精确包、父级包、同语种包和英文默认包。
    /// </summary>
    [Theory]
    [InlineData("fr-CA", "fr-CA")]
    [InlineData("fr-CH", "fr")]
    [InlineData("en-GB", "en-US")]
    [InlineData("zh-HK", "zh-CN")]
    [InlineData("es-AR", "es-ES")]
    [InlineData("ja-JP", "en-US")]
    [InlineData("", "en-US")]
    public void ResolvesSystemLanguageWithDeterministicFallback(string systemLanguageID, string expectedLanguageID)
    {
        using var directory = new TemporaryLocalizationDirectory();
        foreach (var languageID in new[] { "fr-CA", "fr-FR", "fr", "en-AU", "zh-CN", "zh-TW", "es-MX", "es-ES" })
        {
            directory.WriteLanguage(languageID, languageID);
        }

        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Equal(expectedLanguageID,
            catalog.ResolveLanguageID("system", CultureInfo.GetCultureInfo(systemLanguageID)));
    }

    /// <summary>
    /// 系统语言的文字体系父级包优先于同语种的首选地区包。
    /// </summary>
    [Fact]
    public void ResolvesSystemScriptParentBeforePreferredRegionalLanguage()
    {
        using var directory = new TemporaryLocalizationDirectory();
        directory.WriteLanguage("zh-CN", "简体中文");
        directory.WriteLanguage("zh-Hant", "繁體中文");
        var catalog = LocalizationCatalog.Load(directory.Path);

        Assert.Equal("zh-Hant", catalog.ResolveLanguageID("system", CultureInfo.GetCultureInfo("zh-Hant-HK")));
    }
}
