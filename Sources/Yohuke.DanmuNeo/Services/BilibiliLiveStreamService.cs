using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 获取并解析 B 站直播播放流。
/// </summary>
public class BilibiliLiveStreamService : IBilibiliLiveStreamService
{
    /// <inheritdoc/>
    public async Task<LiveStreamPlaySource> GetPlayableSourceAsync(
        string roomId,
        int quality,
        string? cookie,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(roomId, out var parsedRoomId))
        {
            throw LocalizedExceptionText.Create("Services.BilibiliLiveStream.RoomIdInvalid",
                message => new InvalidOperationException(message));
        }

        using var api = new BilibiliApi(cookie ?? "", timeout);
        var response = await api.GetRoomPlayInfoAsync(parsedRoomId, quality, cookie, timeout, cancellationToken);

        if (response.Code != 0)
        {
            var message = response.Message ?? response.Msg;
            throw message is null
                ? LocalizedExceptionText.Create("Services.BilibiliLiveStream.FetchFailed",
                    text => new InvalidOperationException(text))
                : new InvalidOperationException(message);
        }

        return BilibiliLivePlayInfoSelector.Select(response.Data, roomId);
    }
}
