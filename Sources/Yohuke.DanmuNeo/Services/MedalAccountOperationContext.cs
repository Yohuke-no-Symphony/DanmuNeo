using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

internal sealed record MedalAccountOperationContext(BilibiliAccount Account, string Cookie, long Uid,
    CancellationToken CredentialCancellationToken);
