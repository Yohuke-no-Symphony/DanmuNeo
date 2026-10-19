using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 从导航接口验证真实身份，区分明确失效与暂时不可验证。
/// </summary>
public sealed class BilibiliAccountValidationService : IBilibiliAccountValidationService
{
    private readonly HttpClient? httpClient;

    /// <summary>
    /// 初始化验证服务；注入客户端由调用方管理。
    /// </summary>
    public BilibiliAccountValidationService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient;
    }

    /// <inheritdoc/>
    public async Task<AccountValidationResult> ValidateAsync(string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasSendingCredentials(cookie))
        {
            return CreateResult(string.IsNullOrWhiteSpace(cookie)
                ? AccountValidationStatus.NotBound : AccountValidationStatus.Incomplete);
        }

        try
        {
            using var api = new BilibiliApi(cookie, timeout, httpClient);
            var response = await api.GetNavigationAsync(timeout: timeout, cancellationToken: cancellationToken);

            if (response.Code == -101 || response.Code == 0 && response.Data?.IsLogin == false)
            {
                return CreateResult(AccountValidationStatus.Expired);
            }

            if (response.Code != 0 || response.Data is not { IsLogin: true, Uid: > 0 } data ||
                string.IsNullOrWhiteSpace(data.UserName))
            {
                return CreateResult(AccountValidationStatus.Unavailable);
            }

            var configuredUid = ApiCookieHelper.GetValue(cookie, "DedeUserID");
            if (!string.IsNullOrWhiteSpace(configuredUid) &&
                (!long.TryParse(configuredUid, out var uid) || uid != data.Uid))
            {
                return CreateResult(AccountValidationStatus.Incomplete);
            }

            return new()
            {
                Status = AccountValidationStatus.Valid, Uid = data.Uid, UserName = data.UserName,
                CheckedAt = DateTimeOffset.Now
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CreateResult(AccountValidationStatus.Unavailable);
        }
    }

    internal static bool HasSendingCredentials(string cookie)
    {
        return !string.IsNullOrWhiteSpace(ApiCookieHelper.GetValue(cookie, "SESSDATA")) &&
               !string.IsNullOrWhiteSpace(ApiCookieHelper.GetValue(cookie, "bili_jct"));
    }

    private static AccountValidationResult CreateResult(AccountValidationStatus status)
    {
        return new() { Status = status, CheckedAt = DateTimeOffset.Now };
    }
}
