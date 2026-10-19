namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>当前所选账号具有管理权限的规范化房间。</summary>
public sealed class ModerationRoomOption
{
    /// <summary>规范房号。</summary>
    public long RoomId { get; init; }

    /// <summary>本地配置的显示名称。</summary>
    public string RoomName { get; init; } = "";

    /// <summary>账号 UID。</summary>
    public long AccountUid { get; init; }

    /// <summary>主播 UID。</summary>
    public long OwnerUid { get; init; }

    /// <summary>可显示的名称与房号。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(RoomName) ? RoomId.ToString() : $"{RoomName} · {RoomId}";
}
