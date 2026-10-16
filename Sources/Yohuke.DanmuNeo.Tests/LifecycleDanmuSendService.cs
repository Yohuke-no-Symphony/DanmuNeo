using System.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class LifecycleDanmuSendService : IDanmuSendService
{
    /// <inheritdoc/>
    public event EventHandler<DanmuFeedItem>? RecordCreated;

    internal int SubscriberCount => RecordCreated?.GetInvocationList().Length ?? 0;

    internal List<string> SentMessages { get; } = [];

    /// <inheritdoc/>
    public Task<DanmuSendResult> SendAsync(
        string roomId,
        string message,
        BilibiliAccount? account,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SentMessages.Add(message);
        var record = new DanmuFeedItem { UserName = roomId, Content = message, IsSendAccepted = true };
        RecordCreated?.Invoke(this, record);
        return Task.FromResult(new DanmuSendResult { IsAccepted = true, Record = record });
    }

    /// <inheritdoc/>
    public List<string> SplitMessage(string message, int maxLength)
    {
        return [message];
    }
}
