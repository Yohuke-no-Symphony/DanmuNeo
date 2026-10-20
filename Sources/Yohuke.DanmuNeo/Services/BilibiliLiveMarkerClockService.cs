using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Apis.Models.Common;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 用 B 站服务器时间和房间开播时间建立记录文件的一次性参考起点。
/// </summary>
public sealed class BilibiliLiveMarkerClockService : ILiveMarkerClockService
{
    private const long MAX_UNIX_SECONDS = 253402300799;

    private readonly HttpClient? httpClient;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// 初始化取时服务；注入的 HTTP 客户端由调用方管理。
    /// </summary>
    public BilibiliLiveMarkerClockService(HttpClient? httpClient = null, TimeProvider? timeProvider = null)
    {
        this.httpClient = httpClient;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset> CreateStartTimeAsync(string roomId, string? cookie, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!long.TryParse(roomId, out var parsedRoomId) || parsedRoomId <= 0)
        {
            throw LocalizedExceptionText.Create("Services.BilibiliLiveStream.RoomIdInvalid",
                message => new InvalidOperationException(message));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        using var api = new BilibiliApi(httpClient: httpClient, timeout: timeout);
        var serverResponse = await api.GetServerTimeAsync(timeout, cancellationToken).ConfigureAwait(false);
        ThrowIfApiFailed(serverResponse);
        var serverNow = serverResponse.Data?.Now ?? 0;

        if (!IsValidUnixTime(serverNow))
        {
            throw InvalidTime();
        }

        var serverObservedAt = timeProvider.GetTimestamp();
        var roomResponse = await api.GetRoomPlayInfoAsync(parsedRoomId, cookie: cookie, timeout: timeout,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        ThrowIfApiFailed(roomResponse);
        cancellationToken.ThrowIfCancellationRequested();
        var room = roomResponse.Data;

        if (room is null)
        {
            throw InvalidTime();
        }

        if (room.LiveStatus != 1)
        {
            throw LocalizedExceptionText.Create(room.LiveStatus == 2
                    ? "Services.BilibiliLiveStream.RoomReplaying"
                    : "Services.BilibiliLiveStream.RoomOffline",
                message => new InvalidOperationException(message));
        }

        if (!IsValidUnixTime(room.LiveTime))
        {
            throw InvalidTime();
        }

        var elapsed = TimeSpan.FromSeconds(serverNow - room.LiveTime) + timeProvider.GetElapsedTime(serverObservedAt);

        if (elapsed < TimeSpan.Zero)
        {
            throw InvalidTime();
        }

        var localNow = timeProvider.GetLocalNow();

        try
        {
            return localNow - elapsed;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw InvalidTime();
        }
    }

    private static bool IsValidUnixTime(long seconds)
    {
        return seconds is > 0 and <= MAX_UNIX_SECONDS;
    }

    private static void ThrowIfApiFailed<T>(ApiResponse<T> response)
    {
        if (response.Code == 0)
        {
            return;
        }

        var message = string.IsNullOrWhiteSpace(response.Message) ? response.Msg : response.Message;
        throw string.IsNullOrWhiteSpace(message)
            ? LocalizedExceptionText.Create("Services.LiveMarkerClock.FetchFailed",
                text => new InvalidOperationException(text))
            : new InvalidOperationException(message);
    }

    private static InvalidOperationException InvalidTime()
    {
        return LocalizedExceptionText.Create("Services.LiveMarkerClock.InvalidTime",
            message => new InvalidOperationException(message));
    }
}
