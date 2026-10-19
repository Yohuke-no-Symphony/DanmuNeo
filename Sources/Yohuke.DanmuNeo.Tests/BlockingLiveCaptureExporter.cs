using System.Threading;
using Yohuke.DanmuNeo.Models.Captures;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>离线阻塞导出以验证图库操作所有权和重入边界。</summary>
internal sealed class BlockingLiveCaptureExporter : ILiveCaptureExportService
{
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Calls { get; private set; }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ExportAsync(IEnumerable<LiveCaptureRecord> records, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        Started.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
        return ["offline.png"];
    }

    /// <inheritdoc/>
    public async Task ExportToFileAsync(LiveCaptureRecord record, string filePath, CancellationToken cancellationToken = default)
    {
        Calls++;
        Started.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
    }
}
