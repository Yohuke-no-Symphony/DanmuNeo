namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 当前凭据的在线验证状态。
/// </summary>
public enum AccountValidationStatus
{
    NotBound,
    Unchecked,
    Checking,
    Valid,
    Expired,
    Incomplete,
    Unavailable
}
