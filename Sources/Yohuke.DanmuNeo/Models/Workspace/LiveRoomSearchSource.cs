namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>房间查找命中的来源。</summary>
[Flags]
public enum LiveRoomSearchSource
{
    /// <summary>未指定来源。</summary>
    None = 0,
    /// <summary>昵称搜索。</summary>
    Nickname = 1,
    /// <summary>主播 UID 精确查找。</summary>
    UserId = 2,
    /// <summary>直播间号或短号精确查找。</summary>
    RoomId = 4,
    /// <summary>正在直播的关注。</summary>
    Following = 8
}
