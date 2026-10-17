using Yohuke.DanmuNeo.I18n;
using System.Text.Json;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 转换和校验只包含配置的转发规则剪贴板文本。
/// </summary>
public static class DanmuForwardRuleClipboardService
{
    private const int MAX_TEXT_LENGTH = 1024 * 1024;
    private const int MAX_RULE_COUNT = 1000;
    private const int MAX_JSON_DEPTH = 8;
    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        WriteIndented = true
    };
    private static readonly HashSet<string> PACKAGE_FIELDS =
    [
        nameof(DanmuForwardRuleClipboardPackage.Format),
        nameof(DanmuForwardRuleClipboardPackage.Version),
        nameof(DanmuForwardRuleClipboardPackage.Rules)
    ];
    private static readonly HashSet<string> ENTRY_FIELDS =
    [
        nameof(DanmuForwardRuleClipboardEntry.SourceWorkspaceId),
        nameof(DanmuForwardRuleClipboardEntry.SourceRoomStateId),
        nameof(DanmuForwardRuleClipboardEntry.AccountOverrideId),
        nameof(DanmuForwardRuleClipboardEntry.MarkSymbolGroupId),
        nameof(DanmuForwardRuleClipboardEntry.SenderUid),
        nameof(DanmuForwardRuleClipboardEntry.ContentPattern)
    ];

    /// <summary>
    /// 按传入顺序复制规则配置，不携带实例 ID、启用状态或运行数据。
    /// </summary>
    /// <exception cref="InvalidDataException">规则集合为空、含空条目或超过包大小限制。</exception>
    public static string Serialize(IEnumerable<DanmuForwardRuleState> rules)
    {
        if (rules is null)
        {
            throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.NoRulesToCopy",
                message => new InvalidDataException(message));
        }

        var package = new DanmuForwardRuleClipboardPackage();
        var configurationLength = 0L;

        foreach (var rule in rules)
        {
            if (rule is null)
            {
                throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.NullRule",
                    message => new InvalidDataException(message));
            }

            if (package.Rules.Count >= MAX_RULE_COUNT)
            {
                throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.CopyLimit",
                    message => new InvalidDataException(message), MAX_RULE_COUNT);
            }

            var entry = new DanmuForwardRuleClipboardEntry
            {
                SourceWorkspaceId = NormalizeText(rule.SourceWorkspaceId),
                SourceRoomStateId = NormalizeText(rule.SourceRoomStateId),
                AccountOverrideId = NormalizeText(rule.AccountOverrideId),
                MarkSymbolGroupId = NormalizeText(rule.MarkSymbolGroupId),
                SenderUid = NormalizeText(rule.SenderUid),
                ContentPattern = NormalizeText(rule.ContentPattern)
            };
            configurationLength += (long)entry.SourceWorkspaceId.Length + entry.SourceRoomStateId.Length +
                                   entry.AccountOverrideId.Length + entry.MarkSymbolGroupId.Length +
                                   entry.SenderUid.Length + entry.ContentPattern.Length;

            if (configurationLength > MAX_TEXT_LENGTH)
            {
                throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.CopyTooLarge",
                    message => new InvalidDataException(message));
            }

            package.Rules.Add(entry);
        }

        if (package.Rules.Count == 0)
        {
            throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.NoRulesToCopy",
                message => new InvalidDataException(message));
        }

        var text = JsonSerializer.Serialize(package, JSON_OPTIONS);

        if (text.Length > MAX_TEXT_LENGTH)
        {
            throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.CopyTooLarge",
                message => new InvalidDataException(message));
        }

        return text;
    }

    /// <summary>
    /// 整批读取规则配置，为每项创建新的规则 ID 并保持禁用。
    /// </summary>
    /// <exception cref="InvalidDataException">文本不符合协议、包含无效条目或超过包大小限制。</exception>
    public static IReadOnlyList<DanmuForwardRuleState> Deserialize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.ClipboardEmpty",
                message => new InvalidDataException(message));
        }

        if (text.Length > MAX_TEXT_LENGTH)
        {
            throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.ClipboardTooLarge",
                message => new InvalidDataException(message));
        }

        try
        {
            using var document = JsonDocument.Parse(text, new() { MaxDepth = MAX_JSON_DEPTH });
            var root = document.RootElement;
            Func<string> packageContext = () => Localization.Get("Services.ForwardRuleClipboard.PackageContext");
            ValidateObjectFields(root, PACKAGE_FIELDS, packageContext);
            var format = ReadString(root, nameof(DanmuForwardRuleClipboardPackage.Format), packageContext);

            if (format != DanmuForwardRuleClipboardPackage.FORMAT)
            {
                throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.UnsupportedFormat",
                    message => new InvalidDataException(message));
            }

            var version = root.GetProperty(nameof(DanmuForwardRuleClipboardPackage.Version));

            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber) ||
                versionNumber != DanmuForwardRuleClipboardPackage.CURRENT_VERSION)
            {
                throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.UnsupportedVersion",
                    message => new InvalidDataException(message));
            }

            var rules = root.GetProperty(nameof(DanmuForwardRuleClipboardPackage.Rules));

            if (rules.ValueKind != JsonValueKind.Array || rules.GetArrayLength() == 0)
            {
                throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.NonemptyArrayRequired",
                    message => new InvalidDataException(message));
            }

            if (rules.GetArrayLength() > MAX_RULE_COUNT)
            {
                throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.PasteLimit",
                    message => new InvalidDataException(message), MAX_RULE_COUNT);
            }

            var entries = new List<DanmuForwardRuleClipboardEntry>(rules.GetArrayLength());

            foreach (var rule in rules.EnumerateArray())
            {
                var ruleNumber = entries.Count + 1;
                Func<string> context = () => Localization.Format("Services.ForwardRuleClipboard.RuleContext", ruleNumber);
                ValidateObjectFields(rule, ENTRY_FIELDS, context);
                entries.Add(new()
                {
                    SourceWorkspaceId = ReadString(rule, nameof(DanmuForwardRuleClipboardEntry.SourceWorkspaceId), context),
                    SourceRoomStateId = ReadString(rule, nameof(DanmuForwardRuleClipboardEntry.SourceRoomStateId), context),
                    AccountOverrideId = ReadString(rule, nameof(DanmuForwardRuleClipboardEntry.AccountOverrideId), context),
                    MarkSymbolGroupId = ReadString(rule, nameof(DanmuForwardRuleClipboardEntry.MarkSymbolGroupId), context),
                    SenderUid = ReadString(rule, nameof(DanmuForwardRuleClipboardEntry.SenderUid), context),
                    ContentPattern = ReadString(rule, nameof(DanmuForwardRuleClipboardEntry.ContentPattern), context)
                });
            }

            return entries.Select(entry => new DanmuForwardRuleState
            {
                SourceWorkspaceId = entry.SourceWorkspaceId,
                SourceRoomStateId = entry.SourceRoomStateId,
                AccountOverrideId = entry.AccountOverrideId,
                MarkSymbolGroupId = entry.MarkSymbolGroupId,
                SenderUid = entry.SenderUid,
                ContentPattern = entry.ContentPattern
            }).ToArray();
        }
        catch (JsonException exception)
        {
            throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.InvalidJson",
                message => new InvalidDataException(message, exception));
        }
    }

    private static string NormalizeText(string? text)
    {
        return text ?? "";
    }

    private static void ValidateObjectFields(JsonElement value, IReadOnlySet<string> fields, Func<string> context)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.ObjectRequired",
                message => new InvalidDataException(message), context);
        }

        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in value.EnumerateObject())
        {
            if (!fields.Contains(property.Name))
            {
                throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.UnsupportedField",
                    message => new InvalidDataException(message), context);
            }

            if (!found.Add(property.Name))
            {
                throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.DuplicateField",
                    message => new InvalidDataException(message), context);
            }
        }

        if (found.Count != fields.Count)
        {
            throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.MissingField",
                message => new InvalidDataException(message), context);
        }
    }

    private static string ReadString(JsonElement value, string name, Func<string> context)
    {
        var property = value.GetProperty(name);

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()!,
            JsonValueKind.Null => "",
            _ => throw LocalizedExceptionText.Create("Services.ForwardRuleClipboard.StringRequired",
                message => new InvalidDataException(message), context, name)
        };
    }
}
