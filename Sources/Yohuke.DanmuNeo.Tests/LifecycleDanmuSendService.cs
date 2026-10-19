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
    /// <inheritdoc/>
    public Task<DanmuDeliveryResult> SendAsync(DanmuSendRequest request, BilibiliAccount? account,
        AppSettings settings, DanmuDeliveryProgress? continuation = null, CancellationToken cancellationToken = default)
    {
        return TestDanmuDelivery.SendAsync(request, account, settings, continuation, cancellationToken,
            SendAsync, record => RecordCreated?.Invoke(this, record));
    }

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
        return Task.FromResult(new DanmuSendResult { IsAccepted = true, Record = record });
    }
}
