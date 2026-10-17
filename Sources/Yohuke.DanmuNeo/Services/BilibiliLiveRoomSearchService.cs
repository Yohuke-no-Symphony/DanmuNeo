using Yohuke.DanmuNeo.I18n;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 将 B 站直播用户搜索响应转换为可添加的房间信息。
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
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        using var api = new BilibiliApi(httpClient: httpClient);
        var response = await api.SearchLiveUsersAsync(
            keyword.Trim(), cookie: cookie ?? "", timeout: timeout, cancellationToken: cancellationToken, page: page);

        if (response.Code != 0)
        {
            var message = response.Message ?? response.Msg;
            throw message is null
                ? LocalizedExceptionText.Create("Services.BilibiliRoomSearch.SearchFailed",
                    text => new InvalidOperationException(text))
                : new InvalidOperationException(message);
        }

        var data = response.Data;

        if (data?.Result is null)
        {
            throw LocalizedExceptionText.Create("Services.BilibiliRoomSearch.ResultsMissing",
                message => new InvalidOperationException(message));
        }

        var results = data.Result
            .Where(user => user.RoomId > 0 && user.Uid > 0)
            .DistinctBy(user => user.RoomId)
            .Select(user => new LiveRoomSearchResult
            {
                RoomId = user.RoomId.ToString(CultureInfo.InvariantCulture),
                OwnerUid = user.Uid.ToString(CultureInfo.InvariantCulture),
                OwnerName = CleanText(user.Name),
                Title = CleanText(user.Title),
                IsLive = user.IsLive
            })
            .ToList();

        return new()
        {
            Results = results,
            Page = page,
            HasMore = page < data.TotalPages
        };
    }

    private static string CleanText(string? text)
    {
        return WebUtility.HtmlDecode(HtmlTagPattern().Replace(text ?? "", "")).Trim();
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex HtmlTagPattern();
}
