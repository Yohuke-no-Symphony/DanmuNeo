namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>可作为主动禁言目标的公开用户资料。</summary>
public sealed class ModerationUser
{
    /// <summary>用户 UID。</summary>
    public long Uid { get; init; }
    /// <summary>用户昵称。</summary>
    public string UserName { get; init; } = "";
}
