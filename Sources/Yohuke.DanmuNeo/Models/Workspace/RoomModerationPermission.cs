namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 服务端确认的直播间禁言权限。
/// </summary>
public class RoomModerationPermission
{
    /// <summary>
    /// 规范化后的直播间 ID。
    /// </summary>
    public long RoomId { get; init; }

    /// <summary>
    /// 登录账号 UID。
    /// </summary>
    public long AccountUid { get; init; }

    /// <summary>
    /// 主播 UID。
    /// </summary>
    public long OwnerUid { get; init; }

    /// <summary>
    /// 登录账号是否为房管。
    /// </summary>
    public bool IsRoomAdmin { get; init; }

    /// <summary>
    /// 登录账号是否具有禁言权限。
    /// </summary>
    public bool CanModerate => AccountUid > 0 && OwnerUid > 0 && (IsRoomAdmin || AccountUid == OwnerUid);
}
