namespace Yohuke.DanmuNeo.Models.LiveMarker;

/// <summary>保存两个独立时间值的不可变直播记录。</summary>
public sealed record LiveMarkerEntry
{
    public DateTimeOffset RealDateTime { get; init; }
    public TimeSpan LiveTime { get; init; }
    public string Message { get; init; } = "";
    public LiveMarkerColor MarkerColor { get; init; }
}
