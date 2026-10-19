namespace Yohuke.DanmuNeo.Services;

/// <summary>一次有序逻辑发送的目标与各语言正文。</summary>
public sealed record DanmuSendRequest
{
    /// <summary>目标直播间公开 ID。</summary>
    public string RoomId { get; init; } = "";
    /// <summary>按发送顺序排列的正文。</summary>
    public IReadOnlyList<DanmuMessageContent> Contents { get; init; } = [];
}
