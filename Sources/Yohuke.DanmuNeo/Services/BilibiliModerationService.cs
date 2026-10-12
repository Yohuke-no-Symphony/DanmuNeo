using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 通过 B 站直播接口确认权限并执行禁言。
/// </summary>
public class BilibiliModerationService : IBilibiliModerationService
{
    private readonly HttpClient? httpClient;

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
            throw new InvalidOperationException("房间号格式错误");
        }

        using var api = new BilibiliApi(cookie, httpClient: httpClient);
        var userResponse =
            await api.GetUserInfoAsync(parsedRoomId, timeout: timeout, cancellationToken: cancellationToken);

        if (userResponse.Code != 0)
        {
            throw new InvalidOperationException(GetErrorMessage(userResponse.Message, userResponse.Msg, "禁言权限查询失败"));
        }

        var userInfo = userResponse.Data?.Info;

        if (userInfo is null || userInfo.Uid <= 0)
        {
            throw new InvalidOperationException("未获取到登录身份，请重新登录账号");
        }

        var roomResponse = await api.GetRoomPlayInfoAsync(parsedRoomId, cookie: cookie, timeout: timeout,
            cancellationToken: cancellationToken);

        if (roomResponse.Code != 0)
        {
            throw new InvalidOperationException(GetErrorMessage(roomResponse.Message, roomResponse.Msg, "直播间资料查询失败"));
        }

        var roomInfo = roomResponse.Data;

        if (roomInfo is null || roomInfo.Uid <= 0 || roomInfo.RoomId <= 0)
        {
            throw new InvalidOperationException("未获取到直播间身份，无法确认禁言权限");
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

        if (string.IsNullOrWhiteSpace(ApiCookieHelper.GetValue(cookie, "SESSDATA")) ||
            string.IsNullOrWhiteSpace(ApiCookieHelper.GetValue(cookie, "bili_jct")))
        {
            throw new InvalidOperationException("账号登录凭据不完整，请重新登录");
        }

        using var api = new BilibiliApi(cookie, httpClient: httpClient);
        var response =
            await api.AddSilentUserAsync(roomId, userUid, message, timeout: timeout,
                cancellationToken: cancellationToken);

        if (response.Code != 0)
        {
            throw new InvalidOperationException(GetErrorMessage(response.Message, response.Msg, "禁言失败"));
        }
    }

    private static string GetErrorMessage(string? message, string? msg, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        return !string.IsNullOrWhiteSpace(msg) ? msg : fallback;
    }
}
