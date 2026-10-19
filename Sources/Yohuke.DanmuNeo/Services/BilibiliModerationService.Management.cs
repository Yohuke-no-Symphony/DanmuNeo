using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>提供完整名单、解除禁言和主动用户查找。</summary>
public partial class BilibiliModerationService
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<ModerationRecord>> GetMutedUsersAsync(long roomId, string cookie,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(roomId, 1);
        ValidateCredentials(cookie);
        using var api = new BilibiliApi(cookie, httpClient: httpClient);
        var records = new List<ModerationRecord>();
        var recordIds = new HashSet<long>();
        var totalPages = 1;

        for (var page = 1; page <= totalPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await api.GetSilentUserListAsync(roomId, timeout: timeout,
                cancellationToken: cancellationToken, page: page);
            cancellationToken.ThrowIfCancellationRequested();

            if (response.Code != 0)
            {
                throw CreateError(response.Code, response.Message, response.Msg,
                    "Services.BilibiliModeration.ListFailed");
            }

            var data = response.Data;

            if (data?.List is null || data.TotalPages is null or < 0 ||
                (data.TotalPages == 0 && data.List.Count > 0))
            {
                throw InvalidModerationResponse();
            }

            if (page == 1)
            {
                totalPages = data.TotalPages.Value;
            }

            foreach (var user in data.List)
            {
                if (user is null || user.Id <= 0 || user.Uid <= 0)
                {
                    throw InvalidModerationResponse();
                }

                if (recordIds.Add(user.Id))
                {
                    records.Add(new()
                    {
                        RecordId = user.Id,
                        RoomId = roomId,
                        UserUid = user.Uid,
                        UserName = user.Name ?? "",
                        CreatedAtText = user.CreatedAtText ?? "",
                        ExecutorName = user.ExecutorName ?? ""
                    });
                }
            }
        }

        return records;
    }

    /// <inheritdoc/>
    public async Task UnmuteUserAsync(long roomId, long recordId, string cookie,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(roomId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordId, 1);
        ValidateCredentials(cookie);
        using var api = new BilibiliApi(cookie, httpClient: httpClient);
        var response = await api.DeleteSilentUserAsync(roomId, recordId, timeout: timeout,
            cancellationToken: cancellationToken);

        if (response.Code != 0)
        {
            throw CreateError(response.Code, response.Message, response.Msg,
                "Services.BilibiliModeration.UnmuteFailed");
        }

        Changed?.Invoke(this, roomId);
    }

    /// <inheritdoc/>
    public async Task<ModerationUserSearchPage> SearchUsersAsync(string keyword, string? cookie = null, int page = 1,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        cancellationToken.ThrowIfCancellationRequested();
        keyword = keyword.Trim();

        if (keyword.Length == 0)
        {
            return new();
        }

        using var api = new BilibiliApi(cookie ?? "", httpClient: httpClient);

        if (keyword.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(keyword, NumberStyles.None, CultureInfo.InvariantCulture, out var uid) || uid <= 0)
            {
                throw LocalizedExceptionText.Create("Services.BilibiliModeration.UserIdInvalid",
                    text => new InvalidOperationException(text));
            }

            var response = await api.GetUserCardContainerAsync(uid, timeout, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (response.Code != 0)
            {
                throw CreateError(response.Code, response.Message, response.Msg,
                    "Services.BilibiliModeration.UserSearchFailed");
            }

            if (response.Data?.Card is not { Uid: > 0 } user || user.Uid != uid)
            {
                throw InvalidModerationResponse();
            }

            return new() { Users = [new() { Uid = user.Uid, UserName = user.Name ?? "" }] };
        }

        var searchResponse = await api.SearchUsersAsync(keyword, page, timeout, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (searchResponse.Code != 0)
        {
            throw CreateError(searchResponse.Code, searchResponse.Message, searchResponse.Msg,
                "Services.BilibiliModeration.UserSearchFailed");
        }

        var data = searchResponse.Data;

        if (data?.Result is null || data.TotalPages is null or < 0 || (data.TotalPages == 0 && data.Result.Count > 0) || data.Result.Any(user => user is null || user.Uid <= 0))
        {
            throw InvalidModerationResponse();
        }

        return new()
        {
            Users = data.Result.DistinctBy(user => user.Uid).Select(user => new ModerationUser
            {
                Uid = user.Uid,
                UserName = WebUtility.HtmlDecode(Regex.Replace(user.Name ?? "", "<[^>]*>", ""))
            }).ToArray(),
            Page = page,
            HasMore = page < data.TotalPages
        };
    }

    private static InvalidOperationException InvalidModerationResponse()
    {
        return LocalizedExceptionText.Create("Services.BilibiliModeration.ResponseIncomplete",
            text => new InvalidOperationException(text));
    }
}
