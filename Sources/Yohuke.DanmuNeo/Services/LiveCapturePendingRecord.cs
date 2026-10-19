using Yohuke.DanmuNeo.Models.Captures;

namespace Yohuke.DanmuNeo.Services;

/// <summary>原图和元数据尚未完全持久化时保留的可重试状态。</summary>
internal sealed class LiveCapturePendingRecord
{
    public required LiveCaptureRecord Record { get; set; }
    public byte[]? OriginalPng { get; set; }
    public long Revision { get; set; } = 1;
    public long SavedRevision { get; set; }
}
