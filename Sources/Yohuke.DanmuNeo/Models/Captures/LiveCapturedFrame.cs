namespace Yohuke.DanmuNeo.Models.Captures;

/// <summary>已从指定播放会话的视频画面抓取、尚未写入图库的原始 PNG。</summary>
public sealed record LiveCapturedFrame
{
    public required byte[] PngBytes { get; init; }
    public required string RoomId { get; init; }
    public required string BroadcastSessionId { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}
