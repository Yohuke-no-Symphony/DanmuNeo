using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 查询直播间权限并执行禁言。
/// </summary>
public interface IBilibiliModerationService
{
    /// <summary>
    /// 查询指定账号在直播间内的身份和权限。
    /// </summary>
    Task<RoomModerationPermission> GetPermissionAsync(
        string roomId,
        string cookie,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 禁言指定直播间内的用户。
    /// </summary>
    Task MuteUserAsync(
        long roomId,
        long userUid,
        string cookie,
        string message,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
