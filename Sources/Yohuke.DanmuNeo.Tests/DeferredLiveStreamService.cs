using System.Threading;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class DeferredLiveStreamService : IBilibiliLiveStreamService
{
    internal List<TaskCompletionSource<LiveStreamPlaySource>> Requests { get; } = [];

    internal List<CancellationToken> CancellationTokens { get; } = [];

    internal List<string> RoomIds { get; } = [];

    /// <inheritdoc/>
    public Task<LiveStreamPlaySource> GetPlayableSourceAsync(
        string roomId,
        int quality,
        string? cookie,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<LiveStreamPlaySource>(TaskCreationOptions.RunContinuationsAsynchronously);
        Requests.Add(completion);
        CancellationTokens.Add(cancellationToken);
        RoomIds.Add(roomId);
        return completion.Task;
    }
}
