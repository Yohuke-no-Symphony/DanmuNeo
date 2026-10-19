using System.Globalization;
using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Services;

/// <summary>读取近期普通弹幕并转换为统一消息模型。</summary>
public sealed class BilibiliDanmuHistoryService(HttpClient? httpClient = null) : IBilibiliDanmuHistoryService
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<BilibiliDanmuMessage>> GetRecentAsync(string roomId, string cookie,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(roomId, out var parsedRoomId) || parsedRoomId <= 0)
        {
            throw new ArgumentException(Localization.Get("Room.InvalidRoomId"), nameof(roomId));
        }

        using var api = new BilibiliApi(cookie, timeout, httpClient);
        var response = await api.GetDanmuHistoryAsync(parsedRoomId, timeout, cancellationToken);
        if (response.Code != 0 || response.Data is null)
        {
            var detail = AppLogSanitizer.Sanitize(response.Message ?? response.Msg ?? "");
            var error = $"{Localization.Format("Room.History.FailedCode", response.Code)} {detail}".Trim();
            throw response.Code == -101
                ? new BilibiliAuthenticationException(error)
                : new InvalidOperationException(error);
        }

        return (response.Data.Room ?? []).Select(item => ParseItem(roomId, item)).ToList();
    }

    internal static BilibiliDanmuMessage ParseItem(string roomId, BilibiliDanmuHistoryItem item)
    {
        var medal = item.User?["medal"];
        var timestamp = item.Timestamp;
        if (timestamp > 253402300799)
        {
            timestamp /= 1000;
        }

        if (timestamp <= 0 && DateTime.TryParse(item.Timeline, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
        {
            timestamp = new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), TimeSpan.FromHours(8))
                .ToUnixTimeSeconds();
        }

        var result = new BilibiliDanmuMessage
        {
            RoomId = roomId,
            MessageId = item.MessageId ?? "",
            Timestamp = timestamp is > 0 and <= 253402300799 ? timestamp : 0,
            Uid = item.Uid,
            UserName = item.Nickname ?? "",
            Content = item.Text ?? "",
            IsRoomAdmin = item.IsAdmin?.ToString() is "1" or "True" or "true",
            MedalName = medal?["name"]?.Value<string>() ?? "",
            MedalLevel = medal?["level"]?.Value<int>() ?? 0,
            MedalGuardLevel = medal?["guard_level"]?.Value<int>() ?? 0,
            IsEmoticon = item.Emoticon?["text"]?.Value<string>() is { Length: > 0 }
        };
        if (item.Emots is not null)
        {
            foreach (var mapping in item.Emots.Properties())
            {
                var url = mapping.Value["url"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    result.Emoticons.Add(new() { Text = mapping.Name, Url = url });
                }
            }
        }

        if (result.IsEmoticon && item.Emoticon?["url"]?.Value<string>() is { Length: > 0 } emoticonUrl)
        {
            result.Emoticons.Add(new() { Text = result.Content, Url = emoticonUrl });
        }

        return result;
    }
}
