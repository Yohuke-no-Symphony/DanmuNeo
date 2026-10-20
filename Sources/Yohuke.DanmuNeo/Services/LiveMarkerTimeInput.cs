using System.Globalization;
using System.Text.RegularExpressions;

namespace Yohuke.DanmuNeo.Services;

/// <summary>解析和显示不在二十四小时回绕的直播内时间。</summary>
public static partial class LiveMarkerTimeInput
{
    /// <summary>将直播时长格式化为累计小时、分、秒和毫秒。</summary>
    public static string Format(TimeSpan value)
    {
        return $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }

    /// <summary>解析完整的非负直播时间。</summary>
    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = default;
        var match = TimePattern().Match(text?.Trim() ?? "");
        return match.Success && match.Length == text!.Trim().Length && TryRead(match, out value);
    }

    /// <summary>提取输入开头的直播时间及剩余备注。</summary>
    public static bool TryReadPrefix(string text, out TimeSpan value, out string remaining)
    {
        remaining = text;
        value = default;
        var match = TimePattern().Match(text);
        if (!match.Success || match.Length < text.Length && !char.IsWhiteSpace(text[match.Length]) ||
            !TryRead(match, out value))
        {
            return false;
        }

        remaining = text[match.Length..].TrimStart();
        return true;
    }

    private static bool TryRead(Match match, out TimeSpan value)
    {
        value = default;
        if (!long.TryParse(match.Groups[1].Value, out var hours) ||
            !int.TryParse(match.Groups[2].Value, out var minutes) || minutes >= 60 ||
            !int.TryParse(match.Groups[3].Value, out var seconds) || seconds >= 60)
        {
            return false;
        }

        var milliseconds = match.Groups[4].Success
            ? int.Parse(match.Groups[4].Value.PadRight(3, '0'), CultureInfo.InvariantCulture)
            : 0;
        try
        {
            value = TimeSpan.FromTicks(checked(hours * TimeSpan.TicksPerHour +
                minutes * TimeSpan.TicksPerMinute + seconds * TimeSpan.TicksPerSecond +
                milliseconds * TimeSpan.TicksPerMillisecond));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^(\d+)[:.：．](\d{1,2})[:.：．](\d{1,2})(?:[.,](\d{1,3}))?", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();
}
