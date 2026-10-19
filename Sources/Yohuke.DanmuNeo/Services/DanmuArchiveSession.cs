namespace Yohuke.DanmuNeo.Services;

/// <summary>一个直播间标签页的独立监听留档会话。</summary>
public sealed class DanmuArchiveSession
{
    internal DanmuArchiveSession(string roomId, string workspaceId, string roomStateId)
    {
        RoomId = roomId;
        WorkspaceId = workspaceId;
        RoomStateId = roomStateId;
    }

    /// <summary>唯一会话标识。</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");
    /// <summary>公开直播间 ID。</summary>
    public string RoomId { get; }
    /// <summary>工作区 ID 快照。</summary>
    public string WorkspaceId { get; }
    /// <summary>直播间标签页 ID 快照。</summary>
    public string RoomStateId { get; }
    internal bool IsClosed { get; set; }
    internal bool HasFailed { get; set; }
}
