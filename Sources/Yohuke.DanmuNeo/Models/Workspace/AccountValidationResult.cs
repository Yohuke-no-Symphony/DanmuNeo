namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 不含凭据且不持久化的账号验证结果。
/// </summary>
public sealed record AccountValidationResult
{
    /// <summary>
    /// 验证状态。
    /// </summary>
    public AccountValidationStatus Status { get; init; }

    /// <summary>
    /// 服务端确认的用户 UID。
    /// </summary>
    public long Uid { get; init; }

    /// <summary>
    /// 服务端确认的昵称。
    /// </summary>
    public string UserName { get; init; } = "";

    /// <summary>
    /// 最近一次校验结束时间。
    /// </summary>
    public DateTimeOffset? CheckedAt { get; init; }
}
