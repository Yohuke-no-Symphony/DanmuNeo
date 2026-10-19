using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 验证指定凭据的真实登录状态。
/// </summary>
public interface IBilibiliAccountValidationService
{
    /// <summary>
    /// 获取在线验证结果，不修改或清空凭据。
    /// </summary>
    Task<AccountValidationResult> ValidateAsync(string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
