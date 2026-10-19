using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

internal sealed record MedalPendingWearingChange(long? WearingMedalId, AccountMedalCollection Collection,
    CancellationToken CredentialCancellationToken, DateTimeOffset ExpiresAt);
