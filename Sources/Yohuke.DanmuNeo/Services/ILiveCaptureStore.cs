using Yohuke.DanmuNeo.Models.Captures;

namespace Yohuke.DanmuNeo.Services;

/// <summary>永久图库的持久化边界，允许离线控制失败和退出前重试。</summary>
public interface ILiveCaptureStore
{
    IReadOnlyList<LiveCaptureRecord> Records { get; }
    IReadOnlyList<string> LoadWarnings { get; }
    bool HasPendingChanges { get; }
    bool HasPendingOriginals { get; }

    /// <summary>读取独立元数据，保留其他有效记录。</summary>
    Task<IReadOnlyList<LiveCaptureRecord>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>保留原帧以便异步保存和失败重试。</summary>
    LiveCaptureRecord Add(LiveCapturedFrame frame, string roomName);

    /// <summary>更新单条备注。</summary>
    void UpdateNote(string id, string note);

    /// <summary>获取已知记录的原图路径。</summary>
    string GetOriginalPath(string id);

    /// <summary>写完全部待保存内容，失败保持待写状态。</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>判断路径是否位于受保护的永久图库内。</summary>
    bool ContainsPath(string path);
}
