using System.Text.Json;
using System.Text.Json.Nodes;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证转发规则剪贴板协议的内容隔离和整批校验。
/// </summary>
public class DanmuForwardRuleClipboardServiceTests
{
    /// <summary>
    /// 剪贴板只包含协议头和规则配置，不携带实例身份或运行状态。
    /// </summary>
    [Fact]
    public void SerializeContainsOnlyProtocolAndRuleConfiguration()
    {
        var rule = CreateRule("42");
        rule.Id = "original-rule-id";
        rule.IsEnabled = true;

        var text = DanmuForwardRuleClipboardService.Serialize([rule]);

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal("Yohuke.DanmuNeo.ForwardRules", root.GetProperty("Format").GetString());
        Assert.Equal(1, root.GetProperty("Version").GetInt32());
        Assert.Equal(["Format", "Rules", "Version"],
            root.EnumerateObject().Select(property => property.Name).Order().ToArray());
        var entry = root.GetProperty("Rules")[0];
        Assert.Equal(
            ["AccountOverrideId", "ContentPattern", "MarkSymbolGroupId", "SenderUid", "SourceRoomStateId",
                "SourceWorkspaceId"],
            entry.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.DoesNotContain("original-rule-id", text);
        Assert.DoesNotContain("IsEnabled", text);
        Assert.DoesNotContain("Cookie", text);
        Assert.DoesNotContain("StatusText", text);
        Assert.DoesNotContain("SenderUserName", text);
    }

    /// <summary>
    /// 配置顺序和值能往返保存，每次粘贴均创建禁用的新实例。
    /// </summary>
    [Fact]
    public void RoundTripPreservesConfigurationAndCreatesDisabledDistinctIdentities()
    {
        var first = CreateRule("42");
        var second = CreateRule("84");
        first.IsEnabled = true;
        var text = DanmuForwardRuleClipboardService.Serialize([first, second]);

        var imported = DanmuForwardRuleClipboardService.Deserialize(text);
        var importedAgain = DanmuForwardRuleClipboardService.Deserialize(text);

        Assert.Equal(["42", "84"], imported.Select(rule => rule.SenderUid));
        Assert.All(imported, rule =>
        {
            Assert.False(rule.IsEnabled);
            Assert.Equal(first.SourceWorkspaceId, rule.SourceWorkspaceId);
            Assert.Equal(first.SourceRoomStateId, rule.SourceRoomStateId);
            Assert.Equal(first.AccountOverrideId, rule.AccountOverrideId);
            Assert.Equal(first.MarkSymbolGroupId, rule.MarkSymbolGroupId);
            Assert.Equal(first.ContentPattern, rule.ContentPattern);
            Assert.True(Guid.TryParseExact(rule.Id, "N", out _));
        });
        Assert.Equal(6, imported.Concat(importedAgain).Select(rule => rule.Id)
            .Concat([first.Id, second.Id]).Distinct().Count());
        Assert.True(first.IsEnabled);
    }

    /// <summary>
    /// 空字符串和 null 配置允许留待界面编辑，null 统一归为空字符串。
    /// </summary>
    [Fact]
    public void DeserializeNormalizesNullStringsWithoutValidatingBusinessReferences()
    {
        var root = CreatePackageNode();
        var entry = root["Rules"]![0]!.AsObject();

        foreach (var name in entry.Select(property => property.Key).ToArray())
        {
            entry[name] = null;
        }

        var imported = Assert.Single(DanmuForwardRuleClipboardService.Deserialize(root.ToJsonString()));

        Assert.Equal("", imported.SourceWorkspaceId);
        Assert.Equal("", imported.SourceRoomStateId);
        Assert.Equal("", imported.AccountOverrideId);
        Assert.Equal("", imported.MarkSymbolGroupId);
        Assert.Equal("", imported.SenderUid);
        Assert.Equal("", imported.ContentPattern);
        Assert.False(imported.IsEnabled);
    }

    /// <summary>
    /// 损坏文本和错误根节点不能作为转发规则读取。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("不是 JSON")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    public void DeserializeRejectsMalformedTextAndNonObjectRoot(string text)
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            DanmuForwardRuleClipboardService.Deserialize(text));

        Assert.NotEmpty(exception.Message);
    }

    /// <summary>
    /// 协议头必须明确提供，不能依赖 DTO 默认值绕过校验。
    /// </summary>
    [Theory]
    [InlineData("Format")]
    [InlineData("Version")]
    [InlineData("Rules")]
    public void DeserializeRejectsMissingPackageFields(string name)
    {
        var root = CreatePackageNode();
        root.Remove(name);

        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(root.ToJsonString()));
    }

    /// <summary>
    /// 格式名称、版本和值类型必须符合当前协议。
    /// </summary>
    [Theory]
    [InlineData("Format", "null")]
    [InlineData("Format", "1")]
    [InlineData("Format", "\"Other.Format\"")]
    [InlineData("Version", "null")]
    [InlineData("Version", "\"1\"")]
    [InlineData("Version", "true")]
    [InlineData("Version", "1.5")]
    [InlineData("Version", "2")]
    [InlineData("Version", "-1")]
    [InlineData("Version", "99999999999999999999")]
    public void DeserializeRejectsInvalidProtocolHeader(string name, string value)
    {
        var root = CreatePackageNode();
        root[name] = JsonNode.Parse(value);

        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(root.ToJsonString()));
    }

    /// <summary>
    /// 规则列表和每条规则必须有完整的对象结构。
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[1]")]
    [InlineData("[true]")]
    [InlineData("[\"text\"]")]
    public void DeserializeRejectsInvalidRulesStructure(string rules)
    {
        var root = CreatePackageNode();
        root["Rules"] = JsonNode.Parse(rules);

        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(root.ToJsonString()));
    }

    /// <summary>
    /// 每个规则字段只接受字符串或 null。
    /// </summary>
    [Theory]
    [InlineData("SourceWorkspaceId", "1")]
    [InlineData("SourceRoomStateId", "true")]
    [InlineData("AccountOverrideId", "[]")]
    [InlineData("MarkSymbolGroupId", "{}")]
    [InlineData("SenderUid", "42")]
    [InlineData("ContentPattern", "false")]
    public void DeserializeRejectsWrongRuleFieldType(string name, string value)
    {
        var root = CreatePackageNode();
        root["Rules"]![0]![name] = JsonNode.Parse(value);

        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(root.ToJsonString()));
    }

    /// <summary>
    /// 每项规则的协议字段都必须存在。
    /// </summary>
    [Theory]
    [InlineData("SourceWorkspaceId")]
    [InlineData("SourceRoomStateId")]
    [InlineData("AccountOverrideId")]
    [InlineData("MarkSymbolGroupId")]
    [InlineData("SenderUid")]
    [InlineData("ContentPattern")]
    public void DeserializeRejectsMissingRuleField(string name)
    {
        var root = CreatePackageNode();
        root["Rules"]![0]!.AsObject().Remove(name);

        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(root.ToJsonString()));
    }

    /// <summary>
    /// 不接受未知运行字段或重复键，避免隐藏配置覆盖。
    /// </summary>
    [Theory]
    [InlineData("\"Version\": 1, \"Version\": 1", "重复字段")]
    [InlineData("\"Version\": 1, \"Cookie\": \"secret\"", "不支持的字段")]
    public void DeserializeRejectsUnknownAndDuplicatePackageFields(string replacement, string error)
    {
        var text = DanmuForwardRuleClipboardService.Serialize([CreateRule("42")]);
        text = text.Replace("\"Version\": 1", replacement, StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(text));

        Assert.Contains(error, exception.Message);
    }

    /// <summary>
    /// 规则内的重复键不能覆盖先前字段。
    /// </summary>
    [Fact]
    public void DeserializeRejectsDuplicateRuleFields()
    {
        var text = DanmuForwardRuleClipboardService.Serialize([CreateRule("42")]);
        text = text.Replace("\"SenderUid\": \"42\"", "\"SenderUid\": \"42\", \"SenderUid\": \"84\"",
            StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(text));

        Assert.Contains("重复字段", exception.Message);
    }

    /// <summary>
    /// 不能通过规则数据写入启用状态或已有实例 ID。
    /// </summary>
    [Theory]
    [InlineData("Id")]
    [InlineData("IsEnabled")]
    [InlineData("Cookie")]
    public void DeserializeRejectsUnsupportedRuleFields(string name)
    {
        var root = CreatePackageNode();
        root["Rules"]![0]![name] = "untrusted";

        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(root.ToJsonString()));
    }

    /// <summary>
    /// 后续条目错误时整批失败，不静默跳过错误条目。
    /// </summary>
    [Fact]
    public void DeserializeRejectsWholeBatchWhenLaterEntryIsInvalid()
    {
        var root = CreatePackageNode();
        root["Rules"]!.AsArray().Add(null);

        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(root.ToJsonString()));
    }

    /// <summary>
    /// 粘贴文本和复制数据均限制包大小，避免生成无法读取的包。
    /// </summary>
    [Fact]
    public void ClipboardPackageRejectsExcessiveTextLength()
    {
        var rule = CreateRule("42");
        rule.ContentPattern = new('x', 1024 * 1024);
        var root = CreatePackageNode();
        root["Rules"]![0]!["ContentPattern"] = rule.ContentPattern;

        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Serialize([rule]));
        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(root.ToJsonString()));
    }

    /// <summary>
    /// 复制和粘贴都拒绝超量规则。
    /// </summary>
    [Fact]
    public void ClipboardPackageRejectsExcessiveRuleCount()
    {
        var rules = Enumerable.Range(0, 1001).Select(index => CreateRule(index.ToString())).ToList();
        var package = new DanmuForwardRuleClipboardPackage
        {
            Rules = Enumerable.Range(0, 1001).Select(_ => new DanmuForwardRuleClipboardEntry()).ToList()
        };

        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Serialize(rules));
        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(JsonSerializer.Serialize(package)));
    }

    /// <summary>
    /// 空集合或 null 条目不能生成规则包。
    /// </summary>
    [Fact]
    public void SerializeRejectsEmptyOrNullInput()
    {
        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Serialize([]));
        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Serialize(null!));
        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Serialize([null!]));
    }

    /// <summary>
    /// 缺失的剪贴板文本以数据校验错误报告。
    /// </summary>
    [Fact]
    public void DeserializeRejectsNullText()
    {
        Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(null!));
    }

    private static JsonObject CreatePackageNode()
    {
        return JsonNode.Parse(DanmuForwardRuleClipboardService.Serialize([CreateRule("42")]))!.AsObject();
    }

    private static DanmuForwardRuleState CreateRule(string uid)
    {
        return new()
        {
            SourceWorkspaceId = "source-workspace",
            SourceRoomStateId = "source-room",
            AccountOverrideId = "send-account",
            MarkSymbolGroupId = "symbol-group",
            SenderUid = uid,
            ContentPattern = "^【中文.*?】$"
        };
    }
}
