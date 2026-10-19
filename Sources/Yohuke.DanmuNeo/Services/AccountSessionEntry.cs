using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

internal sealed class AccountSessionEntry
{
    internal required BilibiliAccount Account { get; init; }
    internal required string Cookie { get; set; }
    internal AccountValidationResult State { get; set; } = new() { Status = AccountValidationStatus.Unchecked };
    internal CancellationTokenSource Credentials { get; set; } = new();
    internal CancellationTokenSource? Validation { get; set; }
    internal long Revision { get; set; }
    internal bool IsBlocked { get; set; }
}
