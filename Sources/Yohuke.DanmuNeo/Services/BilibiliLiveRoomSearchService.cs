using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Apis.Models.Common;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 将昵称、UID、房号和关注接口转换为可添加的直播间。
/// </summary>
public partial class BilibiliLiveRoomSearchService : IBilibiliLiveRoomSearchService
{
    private readonly HttpClient? httpClient;

    /// <summary>
    /// 初始化搜索服务；注入的 HTTP 客户端由调用方管理。
    /// </summary>
    public BilibiliLiveRoomSearchService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient;
    }

    /// <inheritdoc/>
    public async Task<LiveRoomSearchPage> SearchAsync(
        string keyword,
        int page = 1,
        string? cookie = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        cancellationToken.ThrowIfCancellationRequested();
        keyword = keyword.Trim();
        using var api = new BilibiliApi(cookie ?? "", timeout, httpClient);

        if (keyword.Length == 0)
        {
            if (string.IsNullOrWhiteSpace(cookie))
            {
                return IssuePage(LiveRoomSearchSource.Following,
                    Localization.Get("Services.BilibiliRoomSearch.FollowingLoginRequired"), -101,
                    "Services.BilibiliRoomSearch.FollowingLoginRequired");
            }

            return await TryDiscoverAsync(LiveRoomSearchSource.Following,
                () => GetFollowingAsync(api, timeout, cancellationToken), cancellationToken);
        }

        if (keyword.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(keyword, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            {
                return IssuePage(LiveRoomSearchSource.UserId | LiveRoomSearchSource.RoomId,
                    Localization.Get("Services.BilibiliRoomSearch.InvalidIdentifier"), messageKey: "Services.BilibiliRoomSearch.InvalidIdentifier");
            }

            var pages = await Task.WhenAll(
                TryDiscoverAsync(LiveRoomSearchSource.UserId,
                    () => GetUserRoomAsync(api, number, timeout, cancellationToken), cancellationToken),
                TryDiscoverAsync(LiveRoomSearchSource.RoomId,
                    () => GetRoomAsync(api, number, LiveRoomSearchSource.RoomId, timeout, cancellationToken), cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            var results = new List<LiveRoomSearchResult>();

            foreach (var result in pages.SelectMany(resultPage => resultPage.Results))
            {
                var existing = results.FirstOrDefault(item => item.RoomId == result.RoomId);

                if (existing is null)
                {
                    results.Add(result);
                }
                else
                {
                    existing.Sources |= result.Sources;
                }
            }

            return new() { Results = results, Issues = pages.SelectMany(resultPage => resultPage.Issues).ToList() };
        }

        return await SearchNicknameAsync(api, keyword, page, cookie, timeout, cancellationToken);
    }

    private static async Task<LiveRoomSearchPage> SearchNicknameAsync(
        BilibiliApi api, string keyword, int page, string? cookie, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var response = await api.SearchLiveUsersAsync(
            keyword, cookie: cookie ?? "", timeout: timeout, cancellationToken: cancellationToken, page: page);

        if (response.Code != 0)
        {
            throw new InvalidOperationException(GetError(response));
        }

        var data = response.Data;

        if (data?.Result is null)
        {
            throw LocalizedExceptionText.Create("Services.BilibiliRoomSearch.ResultsMissing",
                message => new InvalidOperationException(message));
        }

        return new()
        {
            Results = data.Result.Where(user => user.RoomId > 0 && user.Uid > 0)
                .DistinctBy(user => user.RoomId)
                .Select(user => new LiveRoomSearchResult
                {
                    RoomId = user.RoomId.ToString(CultureInfo.InvariantCulture),
                    OwnerUid = user.Uid.ToString(CultureInfo.InvariantCulture),
                    OwnerName = CleanText(user.Name),
                    Title = CleanText(user.Title),
                    IsLive = user.IsLive,
                    Sources = LiveRoomSearchSource.Nickname
                }).ToList(),
            Page = page,
            HasMore = page < data.TotalPages
        };
    }

    private static async Task<LiveRoomSearchPage> GetFollowingAsync(
        BilibiliApi api, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var response = await api.GetFollowedLiveRoomsAsync(timeout: timeout, cancellationToken: cancellationToken);

        if (response.Code != 0)
        {
            return IssuePage(LiveRoomSearchSource.Following,
                response.Code == -101
                    ? Localization.Get("Services.BilibiliRoomSearch.FollowingLoginRequired")
                    : GetError(response), response.Code,
                response.Code == -101 ? "Services.BilibiliRoomSearch.FollowingLoginRequired" : null);
        }

        var users = response.Data?.LiveUsers?.Items;

        if (users is null)
        {
            return IssuePage(LiveRoomSearchSource.Following, Localization.Get("Services.BilibiliRoomSearch.ResultsMissing"),
                messageKey: "Services.BilibiliRoomSearch.ResultsMissing");
        }

        return new()
        {
            Results = users.Where(user => user.RoomId > 0 && user.Uid > 0)
                .DistinctBy(user => user.RoomId)
                .Select(user => new LiveRoomSearchResult
                {
                    RoomId = user.RoomId.ToString(CultureInfo.InvariantCulture),
                    OwnerUid = user.Uid.ToString(CultureInfo.InvariantCulture),
                    OwnerName = CleanText(user.Name),
                    IsLive = true,
                    Sources = LiveRoomSearchSource.Following
                }).ToList()
        };
    }

    private static async Task<LiveRoomSearchPage> GetUserRoomAsync(
        BilibiliApi api, long uid, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var response = await api.GetUserCardAsync(uid, timeout, cancellationToken);

        if (response.Code != 0)
        {
            return IssuePage(LiveRoomSearchSource.UserId, GetError(response), response.Code);
        }

        if (response.Data is not { Uid: > 0 } user)
        {
            return IssuePage(LiveRoomSearchSource.UserId, Localization.Get("Services.BilibiliRoomSearch.ResultsMissing"),
                messageKey: "Services.BilibiliRoomSearch.ResultsMissing");
        }

        return user.LiveRoom is { RoomId: > 0 } room
            ? await GetRoomAsync(api, room.RoomId, LiveRoomSearchSource.UserId, timeout, cancellationToken)
            : new();
    }

    private static async Task<LiveRoomSearchPage> GetRoomAsync(
        BilibiliApi api, long roomId, LiveRoomSearchSource source, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var response = await api.GetRoomInfoAsync(roomId, timeout, cancellationToken);

        if (response.Code != 0)
        {
            return IssuePage(source, GetError(response), response.Code);
        }

        if (response.Data?.RoomInfo is not { RoomId: > 0, Uid: > 0 } room)
        {
            return IssuePage(source, Localization.Get("Services.BilibiliRoomSearch.ResultsMissing"),
                messageKey: "Services.BilibiliRoomSearch.ResultsMissing");
        }

        return new()
        {
            Results = [new()
            {
                RoomId = room.RoomId.ToString(CultureInfo.InvariantCulture),
                ShortRoomId = room.ShortId > 0 ? room.ShortId.ToString(CultureInfo.InvariantCulture) : "",
                OwnerUid = room.Uid.ToString(CultureInfo.InvariantCulture),
                OwnerName = CleanText(response.Data.AnchorInfo?.BaseInfo?.Name),
                Title = CleanText(room.Title),
                IsLive = room.LiveStatus == 1,
                Sources = source
            }]
        };
    }

    private static async Task<LiveRoomSearchPage> TryDiscoverAsync(
        LiveRoomSearchSource source, Func<Task<LiveRoomSearchPage>> discoverAsync, CancellationToken cancellationToken)
    {
        try
        {
            var page = await discoverAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return page;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return IssuePage(source, LocalizedExceptionText.Get(exception));
        }
    }

    private static LiveRoomSearchPage IssuePage(LiveRoomSearchSource source, string message, int? apiCode = null, string? messageKey = null)
    {
        return new() { Issues = [new() { Source = source, Message = message, ApiCode = apiCode, MessageKey = messageKey }] };
    }

    private static string GetError<T>(ApiResponse<T> response)
    {
        return response.Message ?? response.Msg ?? Localization.Get("Services.BilibiliRoomSearch.SearchFailed");
    }

    private static string CleanText(string? text)
    {
        return WebUtility.HtmlDecode(HtmlTagPattern().Replace(text ?? "", "")).Trim();
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex HtmlTagPattern();
}
