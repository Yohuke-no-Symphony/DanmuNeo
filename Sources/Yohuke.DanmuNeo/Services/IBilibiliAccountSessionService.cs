using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 管理账号凭据版本及统一发送门禁。
/// </summary>
public interface IBilibiliAccountSessionService : IDisposable
{
    /// <summary>
    /// 指定本地账号的运行时状态发生变化。
    /// </summary>
    event EventHandler<string>? Changed;

    /// <summary>
    /// 注册并观察持久化账号的凭据变更。
    /// </summary>
    void Register(BilibiliAccount account);

    /// <summary>
    /// 删除运行时账号并撤销旧请求。
    /// </summary>
    void Remove(string accountId);

    /// <summary>
    /// 获取当前状态。
    /// </summary>
    AccountValidationResult GetState(string accountId);

    /// <summary>
    /// 验证当前凭据，并隔离旧凭据的迟到响应。
    /// </summary>
    Task<AccountValidationResult> ValidateAsync(BilibiliAccount account, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断当前身份是否可以继续发起发送。
    /// </summary>
    bool CanSend(string accountId, string cookie);

    /// <summary>
    /// 获取随凭据变更、删除或服务释放而取消的令牌。
    /// </summary>
    CancellationToken GetCredentialCancellationToken(string accountId, string cookie);

    /// <summary>
    /// 记录实际请求返回的明确未登录状态。
    /// </summary>
    void ReportAuthenticationFailure(string accountId, string cookie);
}
