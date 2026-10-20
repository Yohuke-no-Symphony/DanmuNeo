using Yohuke.DanmuNeo.Models.LiveMarker;

namespace Yohuke.DanmuNeo.Services;

/// <summary>直播记录文件的独立时间读取、原子保存及应用内写入租约。</summary>
public interface ILiveMarkerFileService
{
    /// <summary>读取旧格式 YAML，不推导或重算两个时间。</summary>
    Task<LiveMarkerDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>先写完同目录临时文件，再原子替换目标文件。</summary>
    Task SaveAsync(LiveMarkerDocument document, string filePath, CancellationToken cancellationToken = default);

    /// <summary>取得文件排他写入租约；同一所有者可以重复取得并逐个释放。</summary>
    IDisposable Acquire(string filePath, object owner);
}
