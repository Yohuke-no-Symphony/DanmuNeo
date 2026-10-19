using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 通过 B 站直播接口确认权限并执行禁言。
/// </summary>
public partial class BilibiliModerationService : IBilibiliModerationService
{
    private readonly HttpClient? httpClient;

    /// <inheritdoc/>
    public event EventHandler<long>? Changed;

    /// <summary>
    /// 初始化服务；注入的 HTTP 客户端由调用方管理。
    /// </summary>
    public BilibiliModerationService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient;
    }

    /// <inheritdoc/>
    public async Task<RoomModerationPermission> GetPermissionAsync(
        string roomId,
        string cookie,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(roomId, out var parsedRoomId) || parsedRoomId <= 0)
        {
            throw LocalizedExceptionText.Create("Services.BilibiliModeration.RoomIdInvalid",
                text => new InvalidOperationException(text));
        }

        using var api = new BilibiliApi(cookie, httpClient: httpClient);
        var userResponse =
            await api.GetUserInfoAsync(parsedRoomId, timeout: timeout, cancellationToken: cancellationToken);

        if (userResponse.Code != 0)
        {
            throw CreateError(userResponse.Code, userResponse.Message, userResponse.Msg, "Services.BilibiliModeration.PermissionQueryFailed");
        }

        var userInfo = userResponse.Data?.Info;

        if (userInfo is null || userInfo.Uid <= 0)
        {
            throw LocalizedExceptionText.Create("Services.BilibiliModeration.AccountIdentityMissing",
                text => new InvalidOperationException(text));
        }

        var roomResponse = await api.GetRoomPlayInfoAsync(parsedRoomId, cookie: cookie, timeout: timeout,
            cancellationToken: cancellationToken);

        if (roomResponse.Code != 0)
        {
            throw CreateError(roomResponse.Code, roomResponse.Message, roomResponse.Msg, "Services.BilibiliModeration.RoomQueryFailed");
        }

        var roomInfo = roomResponse.Data;

        if (roomInfo is null || roomInfo.Uid <= 0 || roomInfo.RoomId <= 0)
        {
            throw LocalizedExceptionText.Create("Services.BilibiliModeration.RoomIdentityMissing",
                text => new InvalidOperationException(text));
        }

        return new()
        {
            RoomId = roomInfo.RoomId,
            OwnerUid = roomInfo.Uid,
            AccountUid = userInfo.Uid,
            IsRoomAdmin = userResponse.Data?.Badge?.IsRoomAdmin == true
        };
    }

    /// <inheritdoc/>
    public async Task MuteUserAsync(
        long roomId,
        long userUid,
        string cookie,
        string message,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(roomId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(userUid, 1);

        ValidateCredentials(cookie);

        using var api = new BilibiliApi(cookie, httpClient: httpClient);
        var response =
            await api.AddSilentUserAsync(roomId, userUid, message, timeout: timeout,
                cancellationToken: cancellationToken);

        if (response.Code != 0)
        {
            throw CreateError(response.Code, response.Message, response.Msg, "Services.BilibiliModeration.MuteFailed");
        }

        Changed?.Invoke(this, roomId);
    }

    private static InvalidOperationException CreateError(int code, string? message, string? msg, string fallbackKey)
    {
        var error = !string.IsNullOrWhiteSpace(message) ? message : msg;

        if (code == -101)
        {
            return string.IsNullOrWhiteSpace(error)
                ? LocalizedExceptionText.Create(fallbackKey, text => new BilibiliAuthenticationException(text))
                : new BilibiliAuthenticationException(error);
        }

        return string.IsNullOrWhiteSpace(error)
            ? LocalizedExceptionText.Create(fallbackKey, text => new InvalidOperationException(text))
            : new InvalidOperationException(error);
    }

    private static void ValidateCredentials(string cookie)
    {
        if (string.IsNullOrWhiteSpace(ApiCookieHelper.GetValue(cookie, "SESSDATA")) ||
            string.IsNullOrWhiteSpace(ApiCookieHelper.GetValue(cookie, "bili_jct")))
        {
            throw LocalizedExceptionText.Create("Services.BilibiliModeration.CredentialsIncomplete",
                text => new InvalidOperationException(text));
        }
    }
}
