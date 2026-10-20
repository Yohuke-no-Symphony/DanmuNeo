namespace Yohuke.DanmuNeo.Models.LiveMarker;

/// <summary>保留旧 YAML 根字段的不可变直播记录文档。</summary>
public sealed record LiveMarkerDocument
{
    public DateTimeOffset StartTime { get; init; }
    public IReadOnlyList<LiveMarkerEntry> Marker { get; init; } = [];
}
