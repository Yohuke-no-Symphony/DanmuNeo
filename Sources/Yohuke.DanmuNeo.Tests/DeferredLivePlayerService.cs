using System.Threading;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class DeferredLivePlayerService : ILivePlayerService
{
    private readonly TaskCompletionSource firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task FirstRequest => firstRequest.Task;

    internal List<TaskCompletionSource<LivePlayerSession>> Requests { get; } = [];

    internal List<CancellationToken> CancellationTokens { get; } = [];

    internal List<string> RevokedTokens { get; } = [];

    /// <inheritdoc/>
    public Task<LivePlayerSession> CreatePlayerAsync(
        LiveStreamPlaySource source,
        string? cookie,
        CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<LivePlayerSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        Requests.Add(completion);
        CancellationTokens.Add(cancellationToken);
        firstRequest.TrySetResult();
        return completion.Task;
    }

    /// <inheritdoc/>
    public void Revoke(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            RevokedTokens.Add(token);
        }
    }
}
