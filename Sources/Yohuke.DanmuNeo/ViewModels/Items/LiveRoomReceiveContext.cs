using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

internal sealed class LiveRoomReceiveContext(ILiveDanmuSocket socket, string roomId, string accountId,
    string cookie, long generation, CancellationToken cancellationToken)
{
    internal ILiveDanmuSocket Socket { get; } = socket;
    internal string RoomId { get; } = roomId;
    internal string AccountId { get; } = accountId;
    internal string Cookie { get; } = cookie;
    internal long Generation { get; } = generation;
    internal CancellationToken CancellationToken { get; } = cancellationToken;
    internal Task HistoryTask { get; set; } = Task.CompletedTask;
    internal bool HistoryRefreshRequested { get; set; }
    internal DanmuArchiveSession? ArchiveSession { get; set; }
}
