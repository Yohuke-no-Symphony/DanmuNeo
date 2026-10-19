using System.Globalization;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Captures;

namespace Yohuke.DanmuNeo.Services;

/// <summary>使用抓图时的墙上时钟计算直播时长，避免媒体缓冲时间冒充整场进度。</summary>
public static class LiveCaptureTimeFormatter
{
    /// <summary>格式化开播后时长，超过一天保留累计小时。</summary>
    public static string FormatElapsed(DateTimeOffset? startedAt, DateTimeOffset capturedAt)
    {
        if (startedAt is null || capturedAt < startedAt.Value)
        {
            return Localization.Get("Captures.Unknown");
        }
        var elapsed = capturedAt - startedAt.Value;
        return $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    /// <summary>格式化具有日期及当地时区偏移的时间。</summary>
    public static string FormatTime(DateTimeOffset? value)
    {
        return value?.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture)
            ?? Localization.Get("Captures.Unknown");
    }

    /// <summary>生成导出图底部的三个时间和当前备注。</summary>
    public static string CreateExportText(LiveCaptureRecord record)
    {
        var lines = new List<string>
        {
            $"{record.RoomName} ({record.RoomId})",
            Localization.Format("Captures.StartedAt", FormatTime(record.StartedAt)),
            Localization.Format("Captures.Elapsed", FormatElapsed(record.StartedAt, record.CapturedAt)),
            Localization.Format("Captures.CapturedAt", FormatTime(record.CapturedAt))
        };
        if (!string.IsNullOrEmpty(record.Note))
        {
            lines.Add(record.Note);
        }
        return string.Join(Environment.NewLine, lines);
    }
}
