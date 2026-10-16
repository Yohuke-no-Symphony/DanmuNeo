using System.Text.RegularExpressions;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 解析歌词时间轴并提供发送文本。
/// </summary>
public class LyricTimelineService
{
    private static readonly Regex TIMELINE_PATTERN = new(@"\[(?<minute>\d+):(?<second>\d+)(?<decimal>\.\d+)?\]", RegexOptions.Compiled);
    private static readonly Regex METADATA_PATTERN = new(@"^\[[a-zA-Z]+:.*\]$", RegexOptions.Compiled);

    /// <summary>
    /// 解析 LRC 歌词文本。
    /// </summary>
    public List<LyricLineState> Parse(string lyricText)
    {
        var lines = new List<LyricLineState>();

        foreach (var rawLine in lyricText.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || METADATA_PATTERN.IsMatch(line))
            {
                continue;
            }

            var matches = TIMELINE_PATTERN.Matches(line);
            var content = TIMELINE_PATTERN.Replace(line, "").Trim();

            if (matches.Count == 0)
            {
                lines.Add(new()
                {
                    TimeSeconds = -1,
                    Timeline = "",
                    Content = content
                });
                continue;
            }

            foreach (Match match in matches)
            {
                var seconds = ParseSeconds(match);
                lines.Add(new()
                {
                    TimeSeconds = seconds,
                    Timeline = FormatTimeline(seconds),
                    Content = content
                });
            }
        }

        return lines.OrderBy(line => line.TimeSeconds < 0 ? double.MaxValue : line.TimeSeconds).ToList();
    }

    /// <summary>
    /// 解析并合并原文与翻译歌词。
    /// </summary>
    public List<LyricLineState> ParseMultilingual(string lyricText, string translatedLyricText)
    {
        var primaryLines = Parse(lyricText);
        var translatedLines = Parse(translatedLyricText);

        if (translatedLines.Count == 0)
        {
            return primaryLines;
        }

        if (primaryLines.Count == 0)
        {
            return translatedLines;
        }

        return HasTimeline(primaryLines) && HasTimeline(translatedLines)
            ? MergeByTimeline(primaryLines, translatedLines)
            : MergeByLineOrder(primaryLines, translatedLines);
    }

    /// <summary>
    /// 生成要发送的歌词弹幕。
    /// </summary>
    public static string CreateMessage(string openMark, string closeMark, string content)
    {
        return $"{openMark}{content}{closeMark}";
    }

    /// <summary>
    /// 合并同一时间轴上的多语言歌词内容。
    /// </summary>
    public static string CreateMultilingualContent(string content, string translatedContent)
    {
        var primary = content.Trim();
        var translated = translatedContent.Trim();

        if (string.IsNullOrWhiteSpace(primary))
        {
            return translated;
        }

        if (string.IsNullOrWhiteSpace(translated) ||
            primary.Equals(translated, StringComparison.Ordinal))
        {
            return primary;
        }

        return $"{primary} / {translated}";
    }

    /// <summary>
    /// 按指定模式生成用于复制的单段歌词文本。
    /// </summary>
    public static string CreateContent(string content, string translatedContent, LyricSendMode mode)
    {
        return mode switch
        {
            LyricSendMode.OriginalOnly => content.Trim(),
            LyricSendMode.TranslationOnly => translatedContent.Trim(),
            _ => CreateMultilingualContent(content, translatedContent)
        };
    }

    /// <summary>
    /// 按指定模式生成独立的歌词发送内容，原文在前，跳过空白和重复文本。
    /// </summary>
    public static IReadOnlyList<string> CreateSendContents(string content, string translatedContent, LyricSendMode mode)
    {
        string[] contents = mode switch
        {
            LyricSendMode.OriginalOnly => [content],
            LyricSendMode.TranslationOnly => [translatedContent],
            _ => [content, translatedContent]
        };

        return contents
            .Select(text => text.Trim())
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 判断歌词是否包含时间轴。
    /// </summary>
    public static bool HasTimeline(IEnumerable<LyricLineState> lines)
    {
        return lines.Any(line => line.TimeSeconds >= 0);
    }

    private static List<LyricLineState> MergeByTimeline(
        List<LyricLineState> primaryLines,
        List<LyricLineState> translatedLines)
    {
        var translatedByTimeline = translatedLines
            .Where(line => line.TimeSeconds >= 0)
            .GroupBy(line => CreateTimelineKey(line.TimeSeconds))
            .ToDictionary(group => group.Key, group => new Queue<LyricLineState>(group));
        var untranslatedLines = new Queue<LyricLineState>(translatedLines.Where(line => line.TimeSeconds < 0));

        foreach (var primaryLine in primaryLines)
        {
            LyricLineState? translatedLine = null;

            if (primaryLine.TimeSeconds >= 0 &&
                translatedByTimeline.TryGetValue(CreateTimelineKey(primaryLine.TimeSeconds), out var candidates) &&
                candidates.Count > 0)
            {
                translatedLine = candidates.Dequeue();
            }
            else if (primaryLine.TimeSeconds < 0 && untranslatedLines.Count > 0)
            {
                translatedLine = untranslatedLines.Dequeue();
            }

            if (translatedLine is not null)
            {
                primaryLine.TranslatedContent = translatedLine.Content;
            }
        }

        var remainingLines = translatedByTimeline.Values
            .SelectMany(queue => queue)
            .Concat(untranslatedLines)
            .Select(line => new LyricLineState
            {
                TimeSeconds = line.TimeSeconds,
                Timeline = line.Timeline,
                TranslatedContent = line.Content
            });

        return primaryLines
            .Concat(remainingLines)
            .OrderBy(line => line.TimeSeconds < 0 ? double.MaxValue : line.TimeSeconds)
            .ToList();
    }

    private static List<LyricLineState> MergeByLineOrder(
        IReadOnlyList<LyricLineState> primaryLines,
        IReadOnlyList<LyricLineState> translatedLines)
    {
        var result = new List<LyricLineState>();
        var count = Math.Max(primaryLines.Count, translatedLines.Count);

        for (var index = 0; index < count; index++)
        {
            var primaryLine = index < primaryLines.Count ? primaryLines[index] : null;
            var translatedLine = index < translatedLines.Count ? translatedLines[index] : null;
            result.Add(new()
            {
                TimeSeconds = primaryLine?.TimeSeconds ?? translatedLine?.TimeSeconds ?? -1,
                Timeline = primaryLine?.Timeline ?? translatedLine?.Timeline ?? "",
                Content = primaryLine?.Content ?? "",
                TranslatedContent = translatedLine?.Content ?? ""
            });
        }

        return result;
    }

    private static long CreateTimelineKey(double seconds)
    {
        return (long)Math.Round(seconds * 100, MidpointRounding.AwayFromZero);
    }

    private static double ParseSeconds(Match match)
    {
        var minute = int.Parse(match.Groups["minute"].Value);
        var second = int.Parse(match.Groups["second"].Value);
        var decimalText = match.Groups["decimal"].Success ? match.Groups["decimal"].Value : "";
        var decimalValue = string.IsNullOrWhiteSpace(decimalText) ? 0 : double.Parse("0" + decimalText);
        return minute * 60 + second + decimalValue;
    }

    private static string FormatTimeline(double seconds)
    {
        if (seconds < 0)
        {
            return "";
        }

        var timeSpan = TimeSpan.FromSeconds(seconds);
        return $"{(int)timeSpan.TotalMinutes:00}:{timeSpan.Seconds:00}";
    }
}
