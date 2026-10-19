using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 查询直播间权限并执行禁言。
/// </summary>
public interface IBilibiliModerationService
{
    /// <summary>禁言或解除成功，通知受影响的规范直播间 ID。</summary>
    event EventHandler<long>? Changed;

    /// <summary>按实际协议读取指定房间的全部禁言页。</summary>
    Task<IReadOnlyList<ModerationRecord>> GetMutedUsersAsync(long roomId, string cookie,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>使用禁言记录 ID 解除禁言。</summary>
    Task UnmuteUserAsync(long roomId, long recordId, string cookie,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>按 UID 精确查询或按昵称分页搜索任意用户。</summary>
    Task<ModerationUserSearchPage> SearchUsersAsync(string keyword, string? cookie = null, int page = 1,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default);

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
