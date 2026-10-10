using System.Threading;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 可控制响应顺序的离线禁言服务。
/// </summary>
public class FakeBilibiliModerationService : IBilibiliModerationService
{
    /// <summary>
    /// 权限请求次数。
    /// </summary>
    public int PermissionRequestCount { get; private set; }

    /// <summary>
    /// 禁言请求记录。
    /// </summary>
    public List<(long RoomId, long UserUid, string Cookie)> MuteRequests { get; } = [];

    /// <summary>
    /// 权限请求令牌。
    /// </summary>
    public List<CancellationToken> PermissionTokens { get; } = [];

    /// <summary>
    /// 权限响应工厂。
    /// </summary>
    public Func<int, Task<RoomModerationPermission>> RespondAsync { get; set; } = _ =>
        Task.FromResult(new RoomModerationPermission
        {
            RoomId = 100,
            AccountUid = 10,
            OwnerUid = 20,
            IsRoomAdmin = true
        });

    /// <summary>
    /// 模拟禁言错误。
    /// </summary>
    public Exception? MuteException { get; set; }

    /// <inheritdoc/>
    public Task<RoomModerationPermission> GetPermissionAsync(
        string roomId, string cookie, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        PermissionTokens.Add(cancellationToken);
        return RespondAsync(++PermissionRequestCount);
    }

    /// <inheritdoc/>
    public Task MuteUserAsync(long roomId, long userUid, string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MuteRequests.Add((roomId, userUid, cookie));
        return MuteException is null ? Task.CompletedTask : Task.FromException(MuteException);
    }
}
