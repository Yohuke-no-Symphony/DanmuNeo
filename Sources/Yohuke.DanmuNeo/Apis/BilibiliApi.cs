using System.Net;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Apis.Models.Common;

namespace Yohuke.DanmuNeo.Apis;

/// <summary>
/// B 站登录与直播间 REST API。
/// </summary>
public class BilibiliApi : BaseApi
{
    private readonly List<string> cookies = [];
    private readonly List<string> csrfs = [];
    private readonly int rnd;
    private readonly BilibiliWbiSigner signer = new();

    /// <summary>
    /// 初始化 B 站 API。
    /// </summary>
    public BilibiliApi(string cookie = "", TimeSpan? timeout = null, HttpClient? httpClient = null)
        : this([cookie], timeout, httpClient)
    {
    }

    /// <summary>
    /// 初始化 B 站 API。
    /// </summary>
    public BilibiliApi(IEnumerable<string> cookies, TimeSpan? timeout = null, HttpClient? httpClient = null)
        : base(httpClient, timeout)
    {
        rnd = DateTimeOffset.UtcNow.ToUnixTimeSeconds() > int.MaxValue
            ? int.MaxValue
            : (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var cookie in cookies)
        {
            this.cookies.Add("");
            csrfs.Add("");
            UpdateCookie(cookie, this.cookies.Count - 1);
        }
    }

    /// <summary>
    /// 更新指定账号槽的 Cookie，并返回标准化后的 Cookie。
    /// </summary>
    public string UpdateCookie(string cookie, int number = 0)
    {
        while (cookies.Count <= number)
        {
            cookies.Add("");
            csrfs.Add("");
        }

        var normalizedCookie = ApiCookieHelper.CreateBilibiliCookie(cookie);
        cookies[number] = normalizedCookie;
        csrfs[number] = ApiCookieHelper.GetValue(normalizedCookie, "bili_jct");
        return normalizedCookie;
    }

    /// <summary>
    /// 获取直播间标题、简介等信息。
    /// </summary>
    public Task<ApiResponse<BilibiliRoomInfoData>> GetRoomInfoAsync(
        long roomId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ApiResponse<BilibiliRoomInfoData>>(
            "https://api.live.bilibili.com/xlive/web-room/v1/index/getInfoByRoom",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId
            },
            CreateLiveHeaders(),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 获取直播间播放地址。
    /// </summary>
    public Task<ApiResponse<BilibiliLivePlayInfoData>> GetRoomPlayInfoAsync(
        long roomId,
        int quality = 0,
        string? cookie = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var headers = CreateLiveHeaders();

        if (!string.IsNullOrWhiteSpace(cookie))
        {
            headers["Cookie"] = ApiCookieHelper.CreateBilibiliCookie(cookie);
        }

        headers["Referer"] = $"https://live.bilibili.com/{roomId}";

        return GetJsonAsync<ApiResponse<BilibiliLivePlayInfoData>>(
            "https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["protocol"] = "0,1",
                ["format"] = "0,1,2",
                ["codec"] = "0,1",
                ["qn"] = quality,
                ["platform"] = "web",
                ["ptype"] = 8,
                ["dolby"] = 5
            },
            headers,
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 获取用户在直播间内的可用弹幕颜色、弹幕位置等信息。
    /// </summary>
    public Task<ApiResponse<BilibiliDanmuConfigData>> GetDanmuConfigAsync(
        long roomId,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ApiResponse<BilibiliDanmuConfigData>>(
            "https://api.live.bilibili.com/xlive/web-room/v1/dM/GetDMConfigByGroup",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId
            },
            CreateAccountHeaders(number),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 获取用户在直播间内的当前弹幕颜色、弹幕位置、发言字数限制等信息。
    /// </summary>
    public Task<ApiResponse<BilibiliUserInfoData>> GetUserInfoAsync(
        long roomId,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ApiResponse<BilibiliUserInfoData>>(
            "https://api.live.bilibili.com/xlive/web-room/v1/index/getInfoByUser",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId
            },
            CreateAccountHeaders(number),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 获取用户公开资料。
    /// </summary>
    public Task<ApiResponse<BilibiliUserCardContainerData>> GetUserCardContainerAsync(
        long uid,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var headers = CreateDefaultHeaders();
        headers["Referer"] = $"https://space.bilibili.com/{uid}";

        return GetJsonAsync<ApiResponse<BilibiliUserCardContainerData>>(
            "https://api.bilibili.com/x/web-interface/card",
            new Dictionary<string, object?>
            {
                ["mid"] = uid
            },
            headers,
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 获取用户公开资料。
    /// </summary>
    public async Task<ApiResponse<BilibiliUserCardData>> GetUserCardAsync(
        long uid,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var headers = CreateDefaultHeaders();
        headers["Referer"] = $"https://space.bilibili.com/{uid}";
        var parameters = await signer.FillAsync(
            new Dictionary<string, object?>
            {
                ["mid"] = uid
            },
            cancellationToken);

        return await GetJsonAsync<ApiResponse<BilibiliUserCardData>>(
            "https://api.bilibili.com/x/space/wbi/acc/info",
            parameters,
            headers,
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 设置用户在直播间内的弹幕颜色或弹幕位置。
    /// </summary>
    public Task<ApiResponse<BilibiliOperationData>> SetDanmuConfigAsync(
        long roomId,
        string? color = null,
        int? mode = null,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return PostFormJsonAsync<ApiResponse<BilibiliOperationData>>(
            "https://api.live.bilibili.com/xlive/web-room/v1/dM/AjaxSetConfig",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["color"] = color,
                ["mode"] = mode,
                ["csrf_token"] = GetCsrf(number),
                ["csrf"] = GetCsrf(number)
            },
            CreateAccountHeaders(number),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 向直播间发送弹幕。
    /// </summary>
    public Task<ApiResponse<BilibiliOperationData>> SendDanmuAsync(
        long roomId,
        string message,
        int mode = 1,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return PostFormJsonAsync<ApiResponse<BilibiliOperationData>>(
            "https://api.live.bilibili.com/msg/send",
            new Dictionary<string, object?>
            {
                ["color"] = 16777215,
                ["fontsize"] = 25,
                ["mode"] = mode,
                ["bubble"] = 0,
                ["msg"] = message,
                ["roomid"] = roomId,
                ["rnd"] = rnd,
                ["csrf_token"] = GetCsrf(number),
                ["csrf"] = GetCsrf(number)
            },
            CreateAccountHeaders(number),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 获取房间被禁言用户列表。
    /// </summary>
    public Task<ApiResponse<BilibiliSilentUserListData>> GetSilentUserListAsync(
        long roomId,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ApiResponse<BilibiliSilentUserListData>>(
            "https://api.live.bilibili.com/xlive/web-ucenter/v1/banned/GetSilentUserList",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["ps"] = 1
            },
            CreateAccountHeaders(number),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 禁言用户。
    /// </summary>
    public Task<ApiResponse<BilibiliOperationData>> AddSilentUserAsync(
        long roomId,
        long uid,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return PostFormJsonAsync<ApiResponse<BilibiliOperationData>>(
            "https://api.live.bilibili.com/xlive/web-ucenter/v1/banned/AddSilentUser",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["tuid"] = uid,
                ["mobile_app"] = "web",
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
    public Task<ApiResponse<BilibiliOperationData>> DeleteSilentUserAsync(
        long roomId,
        long silentId,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return PostFormJsonAsync<ApiResponse<BilibiliOperationData>>(
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

    /// <summary>
    /// 获取房间屏蔽词列表。
    /// </summary>
    public Task<ApiResponse<BilibiliShieldKeywordListData>> GetShieldKeywordListAsync(
        long roomId,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ApiResponse<BilibiliShieldKeywordListData>>(
            "https://api.live.bilibili.com/xlive/web-ucenter/v1/banned/GetShieldKeywordList",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["ps"] = 2
            },
            CreateAccountHeaders(number),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 添加房间屏蔽词。
    /// </summary>
    public Task<ApiResponse<BilibiliOperationData>> AddShieldKeywordAsync(
        long roomId,
        string keyword,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return PostFormJsonAsync<ApiResponse<BilibiliOperationData>>(
            "https://api.live.bilibili.com/xlive/web-ucenter/v1/banned/AddShieldKeyword",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["keyword"] = keyword,
                ["csrf_token"] = GetCsrf(number),
                ["csrf"] = GetCsrf(number)
            },
            CreateAccountHeaders(number),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 删除房间屏蔽词。
    /// </summary>
    public Task<ApiResponse<BilibiliOperationData>> DeleteShieldKeywordAsync(
        long roomId,
        string keyword,
        int number = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return PostFormJsonAsync<ApiResponse<BilibiliOperationData>>(
            "https://api.live.bilibili.com/xlive/web-ucenter/v1/banned/DelShieldKeyword",
            new Dictionary<string, object?>
            {
                ["room_id"] = roomId,
                ["keyword"] = keyword,
                ["csrf_token"] = GetCsrf(number),
                ["csrf"] = GetCsrf(number)
            },
            CreateAccountHeaders(number),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 根据关键字搜索直播用户。
    /// </summary>
    public Task<ApiResponse<BilibiliLiveUserSearchData>> SearchLiveUsersAsync(
        string keyword,
        int pageSize = 42,
        string cookie = "",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        int page = 1)
    {
        var headers = CreateDefaultHeaders();
        headers["Cookie"] = cookie;
        headers["Origin"] = "https://search.bilibili.com";

        return GetJsonAsync<ApiResponse<BilibiliLiveUserSearchData>>(
            "https://api.bilibili.com/x/web-interface/search/type",
            new Dictionary<string, object?>
            {
                ["keyword"] = keyword,
                ["search_type"] = "live_user",
                ["page_size"] = pageSize,
                ["page"] = page
            },
            headers,
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 获取 B 站二维码登录链接。
    /// </summary>
    public Task<ApiResponse<BilibiliLoginQrCodeData>> GetLoginUrlAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ApiResponse<BilibiliLoginQrCodeData>>(
            "https://passport.bilibili.com/x/passport-login/web/qrcode/generate",
            new Dictionary<string, object?>
            {
                ["source"] = "main-fe-header"
            },
            CreateLiveHeaders(),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 检查 B 站二维码登录状态。
    /// </summary>
    public Task<ApiResponse<BilibiliLoginPollData>> GetLoginInfoAsync(
        string qrCodeKey,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ApiResponse<BilibiliLoginPollData>>(
            "https://passport.bilibili.com/x/passport-login/web/qrcode/poll",
            new Dictionary<string, object?>
            {
                ["qrcode_key"] = qrCodeKey,
                ["source"] = "main-fe-header"
            },
            CreateLiveHeaders(),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 访问登录回跳链接并提取 Cookie。
    /// </summary>
    public async Task<BilibiliLoginCookie> GetLoginCookieAsync(
        string url,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var container = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = container,
            AllowAutoRedirect = true,
            UseCookies = true
        };
        using var client = new HttpClient(handler)
        {
            Timeout = timeout ?? DefaultTimeout
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", DEFAULT_USER_AGENT);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Cookie cookie in container.GetAllCookies())
        {
            values[cookie.Name] = cookie.Value;
        }

        values.TryGetValue("buvid3", out var buvid3);
        values.TryGetValue("SESSDATA", out var sessData);
        values.TryGetValue("bili_jct", out var biliJct);
        values.TryGetValue("DedeUserID", out var dedeUserId);
        values.TryGetValue("DedeUserId", out var dedeUserIdLower);

        return new()
        {
            Buvid3 = buvid3 ?? "",
            SessData = sessData ?? "",
            BiliJct = biliJct ?? "",
            DedeUserId = dedeUserId ?? dedeUserIdLower ?? ""
        };
    }

    private Dictionary<string, string> CreateAccountHeaders(int number)
    {
        var headers = CreateLiveHeaders();
        headers["Cookie"] = GetCookie(number);
        return headers;
    }

    private static Dictionary<string, string> CreateLiveHeaders()
    {
        var headers = CreateDefaultHeaders();
        headers["Origin"] = "https://live.bilibili.com";
        headers["Referer"] = "https://live.bilibili.com/";
        return headers;
    }

    private string GetCookie(int number)
    {
        return number >= 0 && number < cookies.Count ? cookies[number] : "";
    }

    private string GetCsrf(int number)
    {
        return number >= 0 && number < csrfs.Count ? csrfs[number] : "";
    }
}
