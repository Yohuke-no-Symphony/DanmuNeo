namespace Yohuke.DanmuNeo.Models.Captures;

/// <summary>单张永久截图的独立元数据；备注与不可变原图分开保存。</summary>
public sealed record LiveCaptureRecord
{
    public int Version { get; init; } = 1;
    public string Id { get; init; } = "";
    public string RoomId { get; init; } = "";
    public string RoomName { get; init; } = "";
    public string BroadcastSessionId { get; init; } = "";
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string OriginalRelativePath { get; init; } = "";
    public string Note { get; init; } = "";
}
