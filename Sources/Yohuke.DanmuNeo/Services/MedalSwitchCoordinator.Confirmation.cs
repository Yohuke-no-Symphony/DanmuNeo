using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

public sealed partial class MedalSwitchCoordinator
{
    private const int CONFIRMATION_ATTEMPTS = 3;
    private static readonly TimeSpan CONFIRMATION_WINDOW = TimeSpan.FromSeconds(30);

    private async Task<AccountMedalCollection> ConfirmWriteAsync(MedalAccountOperationContext context,
        MedalAccountQueue queue, AccountMedalCollection collection, long? wearingMedalId,
        CancellationToken cancellationToken)
    {
        EnsureCurrentCredentials(context);
        collection = ApplyWearingChange(collection, wearingMedalId);
        queue.PendingWearingChange = new(wearingMedalId, collection, context.CredentialCancellationToken,
            timeProvider.GetUtcNow() + CONFIRMATION_WINDOW);
        for (var attempt = 0; attempt < CONFIRMATION_ATTEMPTS; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken).ConfigureAwait(false);
            }
            try
            {
                collection = await ReadAsync(context, queue, cancellationToken).ConfigureAwait(false);
                if (!collection.IsWearingStatePending)
                {
                    return collection;
                }
            }
            catch (Exception exception) when (exception is not BilibiliAuthenticationException &&
                                              !cancellationToken.IsCancellationRequested)
            {
                EnsureCurrentCredentials(context);
            }
        }
        log.Warning(Localization.Get("Medals.Source"), Localization.Get("Medals.ConfirmationPending"));
        return collection;
    }

    private AccountMedalCollection ResolvePendingChange(MedalAccountQueue queue, AccountMedalCollection collection)
    {
        if (queue.PendingWearingChange is not { } pending)
        {
            return collection;
        }
        if (pending.CredentialCancellationToken.IsCancellationRequested)
        {
            queue.PendingWearingChange = null;
            return collection;
        }
        var wearing = collection.Items.Where(item => item.IsWearing).ToArray();
        if (pending.WearingMedalId is { } id
                ? wearing.Length == 1 && wearing[0].Id == id
                : collection.IsComplete && wearing.Length == 0)
        {
            queue.PendingWearingChange = null;
            return collection;
        }
        if (timeProvider.GetUtcNow() >= pending.ExpiresAt)
        {
            queue.PendingWearingChange = null;
            return collection with { WearingSyncState = MedalWearingSyncState.TimedOut };
        }
        if (pending.WearingMedalId is { } expectedId && collection.Items.All(item => item.Id != expectedId))
        {
            collection = pending.Collection with { IsComplete = false };
        }
        collection = ApplyWearingChange(collection, pending.WearingMedalId);
        queue.PendingWearingChange = pending with { Collection = collection };
        return collection;
    }

    private static AccountMedalCollection ApplyWearingChange(AccountMedalCollection collection, long? wearingMedalId)
    {
        return collection with
        {
            Items = collection.Items.Select(item => item with { IsWearing = item.Id == wearingMedalId }).ToArray(),
            WearingSyncState = MedalWearingSyncState.Pending
        };
    }
}
