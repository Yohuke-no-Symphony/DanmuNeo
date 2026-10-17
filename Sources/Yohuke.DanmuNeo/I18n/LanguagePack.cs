using System.Collections.Frozen;

namespace Yohuke.DanmuNeo.I18n;

internal sealed record LanguagePack(LanguageInfo Info, FrozenDictionary<string, string> Strings);
