using System.Threading;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 不访问网络的主播头像服务。
/// </summary>
public class FakeRoomAvatarService : IRoomAvatarService
{
    /// <summary>
    /// 请求的房间 ID。
    /// </summary>
    public List<string> RequestedRooms { get; } = [];

    /// <inheritdoc/>
    public Task<RoomAvatarResult> GetRoomAvatarResultAsync(
        string roomId,
        string ownerUid = "",
        string? cookie = "",
        CancellationToken cancellationToken = default)
    {
        RequestedRooms.Add(roomId);
        return Task.FromResult(new RoomAvatarResult());
    }
}
