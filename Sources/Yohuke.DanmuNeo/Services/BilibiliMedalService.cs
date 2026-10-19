using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Apis.Models.Common;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>通过B站接口读取完整性明确的牌墙并执行佩戴。</summary>
public sealed class BilibiliMedalService(HttpClient? httpClient = null) : IBilibiliMedalService
{
    /// <inheritdoc/>
    public async Task<long> GetRoomOwnerUidAsync(string roomId, string? cookie = null, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(roomId, out var id) || id <= 0)
        {
            throw new ArgumentException(Localization.Get("Room.InvalidRoomId"), nameof(roomId));
        }
        using var api = new BilibiliApi(timeout: timeout, httpClient: httpClient);
        var response = await api.GetRoomInfoAsync(id, timeout, cancellationToken);
        EnsureSuccess(response, false);
        var uid = response.Data?.RoomInfo?.Uid ?? 0;
        if (uid <= 0)
        {
            throw new InvalidOperationException(Localization.Get("Medals.OwnerMissing"));
        }
        return uid;
    }

    /// <inheritdoc/>
    public async Task<AccountMedalCollection> GetMedalsAsync(long accountUid, string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(accountUid, 1);
        using var api = new BilibiliApi(cookie, timeout, httpClient);
        var response = await api.GetMedalWallAsync(accountUid, timeout, cancellationToken);
        EnsureSuccess(response);
        var data = response.Data;
        if (data?.List is null)
        {
            throw new InvalidOperationException(Localization.Get("Medals.WallMissing"));
        }
        var items = new List<AccountMedal>();
        var valid = true;
        var ids = new HashSet<long>();
        foreach (var item in data.List)
        {
            var medal = item?.MedalInfo;
            if (medal is null || medal.MedalId <= 0 || medal.TargetUid <= 0 || !ids.Add(medal.MedalId))
            {
                valid = false;
                continue;
            }
            items.Add(new(medal.MedalId, medal.TargetUid, medal.Name ?? "", Math.Max(0, medal.Level),
                medal.WearingStatus == 1, item?.TargetName ?? ""));
        }
        var total = data.Count ?? data.Total;
        return new(items, valid && total is >= 0 && total == items.Count && data.HasMore != true &&
            items.Count(item => item.IsWearing) <= 1);
    }

    /// <inheritdoc/>
    public Task WearAsync(long medalId, string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) => WriteAsync(medalId, cookie, false, timeout, cancellationToken);

    /// <inheritdoc/>
    public Task TakeOffAsync(long medalId, string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) => WriteAsync(medalId, cookie, true, timeout, cancellationToken);

    private async Task WriteAsync(long medalId, string cookie, bool takeOff, TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(medalId, 1);
        if (string.IsNullOrWhiteSpace(ApiCookieHelper.GetValue(cookie, "bili_jct")))
        {
            throw new InvalidOperationException(Localization.Get("Medals.CredentialsRequired"));
        }
        using var api = new BilibiliApi(cookie, timeout, httpClient);
        var response = takeOff
            ? await api.TakeOffMedalAsync(medalId, timeout, cancellationToken)
            : await api.WearMedalAsync(medalId, timeout, cancellationToken);
        EnsureSuccess(response);
    }

    private static void EnsureSuccess<T>(ApiResponse<T> response, bool authenticated = true)
    {
        if (response.Code == 0)
        {
            return;
        }
        var detail = AppLogSanitizer.Sanitize(response.Message ?? response.Msg ?? "");
        var message = Localization.Format("Medals.ApiFailed", response.Code, detail);
        throw authenticated && response.Code == -101
            ? new BilibiliAuthenticationException(message) : new InvalidOperationException(message);
    }
}
