using System.Globalization;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Services;

/// <summary>按保守 UTF-16 预算分段，并保护完整的 Unicode 文字元素。</summary>
public static class DanmuMessageSegmenter
{
    private const string CONTINUATION_MARK = "…";
    private const string CUT_PUNCTUATION = " 　/，。：！？,.:!?)）】";

    /// <summary>从尚未成功的正文起点创建下一段。</summary>
    public static DanmuSendSegment Next(DanmuMessageContent content, int start, int maxLength, int? preferredLength = null)
    {
        var remaining = content.Body[start..];
        var firstElementLength = StringInfo.GetNextTextElementLength(remaining);
        var minimumLength = content.Prefix.Length + content.Suffix.Length + firstElementLength;
        maxLength = Math.Min(maxLength, Math.Max(preferredLength ?? maxLength, minimumLength));
        var bodyBudget = maxLength - content.Prefix.Length - content.Suffix.Length;
        var continuation = start > 0 && bodyBudget > firstElementLength
            ? CONTINUATION_MARK : "";
        var budget = bodyBudget - continuation.Length;
        var boundaries = StringInfo.ParseCombiningCharacters(remaining);
        var end = 0;
        var punctuationEnd = 0;
        foreach (var boundary in boundaries.Skip(1).Append(remaining.Length))
        {
            if (boundary > budget)
            {
                break;
            }
            end = boundary;
            if (boundary > budget / 2 && CUT_PUNCTUATION.Contains(remaining[boundary - 1]))
            {
                punctuationEnd = boundary;
            }
        }
        if (end == 0)
        {
            throw LocalizedExceptionText.Create("Services.Send.NoBodyBudget", message => new InvalidOperationException(message));
        }
        if (end < remaining.Length && punctuationEnd > 0)
        {
            end = punctuationEnd;
        }
        return new()
        {
            BodyStart = start,
            BodyLength = end,
            Message = content.Prefix + continuation + remaining[..end] + content.Suffix
        };
    }

    internal static int? GetReducedLength(DanmuMessageContent content, DanmuSendSegment rejected, int refreshedLimit)
    {
        var minimumLength = content.Prefix.Length + content.Suffix.Length +
                            StringInfo.GetNextTextElementLength(content.Body.AsSpan(rejected.BodyStart));
        var reducedLength = refreshedLimit > 0 && refreshedLimit < rejected.Message.Length
            ? refreshedLimit
            : Math.Max(minimumLength, rejected.Message.Length - Math.Max(1, rejected.BodyLength / 2));
        return reducedLength >= minimumLength && reducedLength < rejected.Message.Length ? reducedLength : null;
    }
}
