using System.Threading;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class ControlledDanmuHistoryService : IBilibiliDanmuHistoryService
{
    internal Queue<TaskCompletionSource<IReadOnlyList<BilibiliDanmuMessage>>> Requests { get; } = new();
    internal int Count { get; private set; }

    /// <inheritdoc/>
    public Task<IReadOnlyList<BilibiliDanmuMessage>> GetRecentAsync(string roomId, string cookie,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        Count++;
        var result = new TaskCompletionSource<IReadOnlyList<BilibiliDanmuMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Requests.Enqueue(result);
        return result.Task;
    }
}
