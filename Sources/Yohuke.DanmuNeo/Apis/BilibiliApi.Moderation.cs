using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Apis.Models.Common;

namespace Yohuke.DanmuNeo.Apis;

/// <summary>直播间禁言名单、写操作与目标用户查询。</summary>
public partial class BilibiliApi
{
    /// <summary>
    /// 获取房间被禁言用户列表。
    /// </summary>
    public Task<ApiResponse<BilibiliSilentUserListData>> GetSilentUserListAsync(
        long roomId,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        int page = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        return PostFormJsonAsync<ApiResponse<BilibiliSilentUserListData>>(
            "https://api.live.bilibili.com/xlive/web-ucenter/v1/banned/GetSilentUserList",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["ps"] = page,
                ["visit_id"] = "",
                ["csrf_token"] = GetCsrf(number),
                ["csrf"] = GetCsrf(number)
            },
            CreateAccountHeaders(number),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 禁言用户。
    /// </summary>
    public Task<ApiResponse<JToken>> AddSilentUserAsync(
        long roomId,
        long uid,
        string message,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return PostFormJsonAsync<ApiResponse<JToken>>(
            "https://api.live.bilibili.com/xlive/web-ucenter/v1/banned/AddSilentUser",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["tuid"] = uid,
                ["mobile_app"] = "web",
                ["msg"] = message,
                ["csrf_token"] = GetCsrf(number),
                ["csrf"] = GetCsrf(number)
            },
            CreateAccountHeaders(number),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 解除用户禁言。
    /// </summary>
    public Task<ApiResponse<JToken>> DeleteSilentUserAsync(
        long roomId,
        long silentId,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return PostFormJsonAsync<ApiResponse<JToken>>(
            "https://api.live.bilibili.com/banned_service/v1/Silent/del_room_block_user",
            new Dictionary<string, object?>
            {
                ["roomid"] = roomId,
                ["id"] = silentId,
                ["csrf_token"] = GetCsrf(number),
                ["csrf"] = GetCsrf(number)
            },
            CreateAccountHeaders(number),
            timeout,
            cancellationToken);
    }

    /// <summary>按昵称分页查找全部 B 站用户，包括没有直播间的用户。</summary>
    public Task<ApiResponse<BilibiliUserSearchData>> SearchUsersAsync(
        string keyword,
        int page = 1,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        return GetJsonAsync<ApiResponse<BilibiliUserSearchData>>(
            "https://api.bilibili.com/x/web-interface/search/type",
            new Dictionary<string, object?>
            {
                ["search_type"] = "bili_user",
                ["keyword"] = keyword,
                ["page"] = page,
                ["page_size"] = 20
            },
            CreateAccountHeaders(0),
            timeout,
            cancellationToken);
    }
}
