namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>一条属于特定直播间的禁言记录。</summary>
public sealed class ModerationRecord
{
    /// <summary>禁言记录 ID，用于解除禁言。</summary>
    public long RecordId { get; init; }
    /// <summary>规范直播间 ID。</summary>
    public long RoomId { get; init; }
    /// <summary>当前配置中的直播间显示名称。</summary>
    public string RoomName { get; set; } = "";
    /// <summary>被禁言用户 UID。</summary>
    public long UserUid { get; init; }
    /// <summary>被禁言用户名称。</summary>
    public string UserName { get; init; } = "";
    /// <summary>接口返回的禁言时间文本。</summary>
    public string CreatedAtText { get; init; } = "";
    /// <summary>执行人名称。</summary>
    public string ExecutorName { get; init; } = "";
    /// <summary>直播间显示说明。</summary>
    public string RoomLabel => string.IsNullOrWhiteSpace(RoomName) ? RoomId.ToString() : $"{RoomName} · {RoomId}";
}
