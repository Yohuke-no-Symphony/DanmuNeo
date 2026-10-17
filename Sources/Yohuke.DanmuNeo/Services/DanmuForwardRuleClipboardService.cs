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
            throw new InvalidDataException("没有可复制的转发规则。");
        }

        var package = new DanmuForwardRuleClipboardPackage();
        var configurationLength = 0L;

        foreach (var rule in rules)
        {
            if (rule is null)
            {
                throw new InvalidDataException("转发规则包含空条目，无法复制。");
            }

            if (package.Rules.Count >= MAX_RULE_COUNT)
            {
                throw new InvalidDataException($"一次最多复制 {MAX_RULE_COUNT} 条转发规则。");
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
                throw new InvalidDataException("转发规则内容过大，请减少复制的规则数量或内容。");
            }

            package.Rules.Add(entry);
        }

        if (package.Rules.Count == 0)
        {
            throw new InvalidDataException("没有可复制的转发规则。");
        }

        var text = JsonSerializer.Serialize(package, JSON_OPTIONS);

        if (text.Length > MAX_TEXT_LENGTH)
        {
            throw new InvalidDataException("转发规则内容过大，请减少复制的规则数量或内容。");
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
            throw new InvalidDataException("剪贴板中没有转发规则文本。");
        }

        if (text.Length > MAX_TEXT_LENGTH)
        {
            throw new InvalidDataException("剪贴板中的转发规则内容过大。");
        }

        try
        {
            using var document = JsonDocument.Parse(text, new() { MaxDepth = MAX_JSON_DEPTH });
            var root = document.RootElement;
            ValidateObjectFields(root, PACKAGE_FIELDS, "转发规则配置包");
            var format = ReadString(root, nameof(DanmuForwardRuleClipboardPackage.Format), "转发规则配置包");

            if (format != DanmuForwardRuleClipboardPackage.FORMAT)
            {
                throw new InvalidDataException("剪贴板内容不是支持的转发规则格式。");
            }

            var version = root.GetProperty(nameof(DanmuForwardRuleClipboardPackage.Version));

            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber) ||
                versionNumber != DanmuForwardRuleClipboardPackage.CURRENT_VERSION)
            {
                throw new InvalidDataException("不支持此转发规则格式版本。");
            }

            var rules = root.GetProperty(nameof(DanmuForwardRuleClipboardPackage.Rules));

            if (rules.ValueKind != JsonValueKind.Array || rules.GetArrayLength() == 0)
            {
                throw new InvalidDataException("转发规则列表必须是非空数组。");
            }

            if (rules.GetArrayLength() > MAX_RULE_COUNT)
            {
                throw new InvalidDataException($"一次最多粘贴 {MAX_RULE_COUNT} 条转发规则。");
            }

            var entries = new List<DanmuForwardRuleClipboardEntry>(rules.GetArrayLength());

            foreach (var rule in rules.EnumerateArray())
            {
                var context = $"第 {entries.Count + 1} 条转发规则";
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
            throw new InvalidDataException("剪贴板中的转发规则文本不是有效的 JSON 配置。", exception);
        }
    }

    private static string NormalizeText(string? text)
    {
        return text ?? "";
    }

    private static void ValidateObjectFields(JsonElement value, IReadOnlySet<string> fields, string context)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{context}必须是完整对象，不能是空条目或其他类型。");
        }

        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in value.EnumerateObject())
        {
            if (!fields.Contains(property.Name))
            {
                throw new InvalidDataException($"{context}包含不支持的字段。");
            }

            if (!found.Add(property.Name))
            {
                throw new InvalidDataException($"{context}包含重复字段。");
            }
        }

        if (found.Count != fields.Count)
        {
            throw new InvalidDataException($"{context}缺少必要字段。");
        }
    }

    private static string ReadString(JsonElement value, string name, string context)
    {
        var property = value.GetProperty(name);

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()!,
            JsonValueKind.Null => "",
            _ => throw new InvalidDataException($"{context}的 {name} 必须是字符串或 null。")
        };
    }
}
