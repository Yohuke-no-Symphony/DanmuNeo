using System.Threading;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class MedalValidationService : IBilibiliAccountValidationService
{
    /// <inheritdoc/>
    public Task<AccountValidationResult> ValidateAsync(string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AccountValidationResult { Status = AccountValidationStatus.Valid, Uid = 42 });
    }
}
