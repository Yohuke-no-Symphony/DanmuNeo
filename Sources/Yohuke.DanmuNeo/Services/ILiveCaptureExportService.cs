using Yohuke.DanmuNeo.Models.Captures;

namespace Yohuke.DanmuNeo.Services;

/// <summary>不破坏永久原图和记录的 PNG 合成导出边界。</summary>
public interface ILiveCaptureExportService
{
    /// <summary>将指定记录合成到目录中的新 PNG 文件。</summary>
    Task<IReadOnlyList<string>> ExportAsync(IEnumerable<LiveCaptureRecord> records, string outputDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>将一条记录合成到用户选择的 PNG 文件。</summary>
    Task ExportToFileAsync(LiveCaptureRecord record, string filePath, CancellationToken cancellationToken = default);
}
