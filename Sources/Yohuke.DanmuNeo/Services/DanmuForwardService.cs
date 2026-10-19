using Yohuke.DanmuNeo.I18n;
using System.Text.RegularExpressions;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 提供弹幕转发过滤逻辑。
/// </summary>
public class DanmuForwardService
{
    /// <summary>
    /// 判断弹幕是否满足转发规则。
    /// </summary>
    public bool IsMatch(BilibiliDanmuMessage message, DanmuForwardRuleState rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.SenderUid) &&
            (!long.TryParse(rule.SenderUid.Trim(), out var uid) || message.Uid != uid))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.ContentPattern))
        {
            return true;
        }

        return Regex.IsMatch(message.Content, rule.ContentPattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
    }

    /// <summary>
    /// 校验规则正则。
    /// </summary>
    public string Validate(DanmuForwardRuleState rule)
    {
        if (GetValidationErrorKey(rule) is { } errorKey)
        {
            return Localization.Get(errorKey);
        }

        if (string.IsNullOrWhiteSpace(rule.ContentPattern))
        {
            return "";
        }

        try
        {
            _ = new Regex(rule.ContentPattern);
            return "";
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    internal string? GetValidationErrorKey(DanmuForwardRuleState rule)
    {
        return string.IsNullOrWhiteSpace(rule.SenderUid) && string.IsNullOrWhiteSpace(rule.ContentPattern)
            ? "Services.DanmuForward.FilterRequired"
            : null;
    }

    /// <summary>
    /// 创建最终转发弹幕文本。
    /// </summary>
    public string CreateForwardMessage(
        BilibiliDanmuMessage message,
        DanmuForwardRuleState rule,
        MarkSymbolGroup fallbackGroup,
        IEnumerable<MarkSymbolGroup> symbolGroups)
    {
        var content = CreateForwardContent(message, rule, fallbackGroup, symbolGroups);
        return content.Prefix + content.Body + content.Suffix;
    }
    /// <summary>创建保留正文和完整标识符边界的转发请求内容。</summary>
    public DanmuMessageContent CreateForwardContent(BilibiliDanmuMessage message, DanmuForwardRuleState rule,
        MarkSymbolGroup fallbackGroup, IEnumerable<MarkSymbolGroup> symbolGroups)
    {
        var marks = symbolGroups.FirstOrDefault(group => group.Id == rule.MarkSymbolGroupId) ?? fallbackGroup;
        return new()
        {
            Language = "forward", Body = message.Content.Trim(), Prefix = marks.TranslateOpenMark, Suffix = marks.TranslateCloseMark
        };
    }
}
