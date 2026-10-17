using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Yohuke.DanmuNeo.Apis.Models.Music;

namespace Yohuke.DanmuNeo.Apis;

/// <summary>
/// QQ 音乐登录、搜索、歌词和歌曲详情 API。
/// </summary>
public class QQMusicApi : BaseApi
{
    private readonly CookieContainer cookieContainer;
    private string ptQrToken = "";
    private string loginSig = "";

    /// <summary>
    /// 初始化 QQ 音乐 API；注入的 HTTP 客户端由调用方管理。
    /// </summary>
    public QQMusicApi(string cookie = "", TimeSpan? timeout = null, HttpClient? httpClient = null)
        : this(new(), cookie, timeout, httpClient)
    {
    }

    private QQMusicApi(CookieContainer cookieContainer, string cookie, TimeSpan? timeout, HttpClient? httpClient)
        : base(httpClient ?? CreateClient(cookieContainer), timeout)
    {
        this.cookieContainer = cookieContainer;
        SetCookie(cookie);
    }

    /// <summary>
    /// 获取 pt_login_sig。
    /// </summary>
    public async Task PrepareLoginAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await GetStringAsync(
            "https://xui.ptlogin2.qq.com/cgi-bin/xlogin",
            new Dictionary<string, object?>
            {
                ["appid"] = 716027609,
                ["daid"] = 383,
                ["style"] = 33,
                ["login_text"] = "登录",
                ["hide_title_bar"] = 1,
                ["hide_border"] = 1,
                ["target"] = "self",
                ["s_url"] = "https://graph.qq.com/oauth2.0/login_jump",
                ["pt_3rd_aid"] = 100497308,
                ["pt_feedback_link"] = "https://support.qq.com/products/77942?customInfo=.appid100497308",
                ["theme"] = 2,
                ["verify_theme"] = ""
            },
            CreateDefaultHeaders(),
            timeout,
            cancellationToken);

        loginSig = GetCookieValue("https://xui.ptlogin2.qq.com", "pt_login_sig");
    }

    /// <summary>
    /// 获取登录二维码图片。
    /// </summary>
    public async Task<QQMusicQrCodeResponse> GetLoginQrCodeAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(loginSig))
        {
            await PrepareLoginAsync(timeout, cancellationToken);
        }

        using var response = await GetResponseAsync(
            "https://ssl.ptlogin2.qq.com/ptqrshow",
            new Dictionary<string, object?>
            {
                ["appid"] = 716027609,
                ["e"] = 2,
                ["l"] = "M",
                ["s"] = 3,
                ["d"] = 72,
                ["v"] = 4,
                ["t"] = Random.Shared.NextDouble(),
                ["daid"] = 383,
                ["pt_3rd_aid"] = 100497308
            },
            CreateLoginHeaders(),
            timeout,
            cancellationToken);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var qrSig = GetCookieValue("https://ssl.ptlogin2.qq.com", "qrsig");
        ptQrToken = GetToken(qrSig, 0).ToString();

        return new()
        {
            ImageBytes = bytes,
            QrSig = qrSig,
            PtQrToken = int.Parse(ptQrToken)
        };
    }

    /// <summary>
    /// 查询扫码登录状态。
    /// </summary>
    public async Task<QQMusicLoginStatus> GetLoginInfoAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var text = await GetStringAsync(
            "https://ssl.ptlogin2.qq.com/ptqrlogin",
            new Dictionary<string, object?>
            {
                ["u1"] = "https://graph.qq.com/oauth2.0/login_jump",
                ["ptqrtoken"] = ptQrToken,
                ["ptredirect"] = 0,
                ["h"] = 1,
                ["t"] = 1,
                ["g"] = 1,
                ["from_ui"] = 1,
                ["ptlang"] = 2052,
                ["action"] = $"0-0-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                ["js_ver"] = 22071217,
                ["js_type"] = 1,
                ["login_sig"] = loginSig,
                ["pt_uistyle"] = 40,
                ["aid"] = 716027609,
                ["daid"] = 383,
                ["pt_3rd_aid"] = 100497308,
                ["has_onekey"] = 1,
                ["o1vId"] = "f49360ebaddf6358d888317a4e8aa604"
            },
            CreateLoginHeaders(),
            timeout,
            cancellationToken);

        return ParseLoginStatus(text);
    }

    /// <summary>
    /// 扫码确认后授权 QQ 音乐登录。
    /// </summary>
    public async Task AuthorizeAsync(
        string checkSigUrl,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        await FollowRedirectAsync(checkSigUrl, timeout, cancellationToken);
        var pSkey = GetCookieValue("https://graph.qq.com", "p_skey");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.qq.com/oauth2.0/authorize");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = "100497308",
            ["redirect_uri"] = "https://y.qq.com/portal/wx_redirect.html?login_type=1&surl=https://y.qq.com/",
            ["scope"] = "all",
            ["state"] = "state",
            ["switch"] = "",
            ["from_ptlogin"] = "1",
            ["src"] = "1",
            ["update_auth"] = "1",
            ["openapi"] = "80901010_1030",
            ["g_tk"] = GetToken(pSkey).ToString(),
            ["auth_time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            ["ui"] = ""
        });

        foreach (var pair in CreateDefaultHeaders())
        {
            request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }

        using var response = await Client.SendAsync(request, cancellationToken);
        var location = response.Headers.Location?.ToString() ?? "";
        var match = Regex.Match(location, "code=([0-9A-F]+)", RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            throw new InvalidOperationException("QQ 音乐授权回跳链接中没有 code。");
        }

        var jsonData = new
        {
            comm = new
            {
                g_tk = 5381,
                platform = "yqq",
                ct = 24,
                cv = 0
            },
            req = new
            {
                module = "QQConnectLogin.LoginServer",
                method = "QQLogin",
                param = new
                {
                    code = match.Groups[1].Value
                }
            }
        };

        await PostTextJsonAsync<object>(
            "https://u.y.qq.com/cgi-bin/musicu.fcg",
            JsonConvert.SerializeObject(jsonData),
            CreateMusicHeaders(),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 按关键字搜索歌曲，使用旧版无需登录接口。
    /// </summary>
    public Task<QQMusicSearchV1Response> SearchSongsV1Async(
        string keyword,
        int limit = 10,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<QQMusicSearchV1Response>(
            "https://c.y.qq.com/soso/fcgi-bin/client_search_cp",
            new Dictionary<string, object?>
            {
                ["w"] = keyword,
                ["n"] = limit,
                ["format"] = "json"
            },
            CreateMusicHeaders(),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 按关键字搜索歌曲，使用新版登录接口。
    /// </summary>
    public Task<QQMusicSearchV2Response> SearchSongsV2Async(
        string keyword,
        int limit = 10,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var jsonData = new
        {
            comm = new
            {
                cv = 4747474,
                ct = 24,
                format = "json",
                inCharset = "utf-8",
                outCharset = "utf-8",
                notice = 0,
                platform = "yqq.json",
                needNewCode = 1,
                uin = 0,
                g_tk_new_20200303 = 1244134330,
                g_tk = 1244134330
            },
            req_1 = new
            {
                method = "DoSearchForQQMusicDesktop",
                module = "music.search.SearchCgiService",
                param = new
                {
                    remoteplace = "txt.yqq.top",
                    searchid = "",
                    search_type = 0,
                    query = keyword,
                    page_num = 1,
                    num_per_page = limit
                }
            }
        };

        return PostTextJsonAsync<QQMusicSearchV2Response>(
            "https://u.y.qq.com/cgi-bin/musicu.fcg",
            JsonConvert.SerializeObject(jsonData),
            CreateMusicHeaders(),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 根据歌曲 MID 获取歌词。
    /// </summary>
    public Task<QQMusicLyricResponse> GetLyricAsync(
        string songMid,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<QQMusicLyricResponse>(
            "https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg",
            new Dictionary<string, object?>
            {
                ["songmid"] = songMid,
                ["nobase64"] = 1,
                ["g_tk"] = 5381,
                ["format"] = "json"
            },
            CreateMusicHeaders(),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 根据歌曲 ID 获取歌曲信息。
    /// </summary>
    public Task<QQMusicSongInfoResponse> GetSongInfoAsync(
        long songId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<QQMusicSongInfoResponse>(
            "https://c.y.qq.com/v8/fcg-bin/fcg_play_single_song.fcg",
            new Dictionary<string, object?>
            {
                ["songid"] = songId,
                ["format"] = "json"
            },
            CreateMusicHeaders(),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 获取 QQ 音乐 Cookie。
    /// </summary>
    public QQMusicCookie GetCookie()
    {
        return new()
        {
            Uin = GetCookieValue("https://y.qq.com", "uin"),
            QmKeyst = GetCookieValue("https://y.qq.com", "qm_keyst")
        };
    }

    /// <summary>
    /// 设置 QQ 音乐 Cookie。
    /// </summary>
    public void SetCookie(string cookie)
    {
        var values = ApiCookieHelper.Parse(cookie);

        if (values.TryGetValue("uin", out var uin))
        {
            cookieContainer.Add(new Uri("https://y.qq.com"), new Cookie("uin", uin));
        }

        if (values.TryGetValue("qm_keyst", out var qmKeyst))
        {
            cookieContainer.Add(new Uri("https://y.qq.com"), new Cookie("qm_keyst", qmKeyst));
        }
    }

    /// <summary>
    /// 计算 QQ 音乐 g_tk 类令牌。
    /// </summary>
    public static int GetToken(string? value, int offset = 5381)
    {
        var result = offset;

        if (string.IsNullOrEmpty(value))
        {
            return result;
        }

        foreach (var character in value)
        {
            result += (result << 5) + character;
        }

        return 0x7fffffff & result;
    }

    private static HttpClient CreateClient(CookieContainer cookieContainer)
    {
        return new(new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = false
        });
    }

    private static Dictionary<string, string> CreateMusicHeaders()
    {
        var headers = CreateDefaultHeaders();
        headers["Referer"] = "https://y.qq.com/";
        headers["Origin"] = "https://y.qq.com";
        return headers;
    }

    private static Dictionary<string, string> CreateLoginHeaders()
    {
        var headers = CreateDefaultHeaders();
        headers["Host"] = "ssl.ptlogin2.qq.com";
        headers["Referer"] = "https://xui.ptlogin2.qq.com/";
        return headers;
    }

    private string GetCookieValue(string url, string name)
    {
        return cookieContainer.GetCookies(new Uri(url))[name]?.Value ?? "";
    }

    private async Task FollowRedirectAsync(string url, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true
        };
        using var client = new HttpClient(handler)
        {
            Timeout = timeout ?? DefaultTimeout
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", DEFAULT_USER_AGENT);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static QQMusicLoginStatus ParseLoginStatus(string text)
    {
        var match = Regex.Match(text, @"ptuiCB\('(?<code>[^']*)','(?<subCode>[^']*)','(?<url>[^']*)','(?<unused>[^']*)','(?<message>[^']*)'", RegexOptions.Singleline);

        if (!match.Success)
        {
            return new()
            {
                RawText = text
            };
        }

        return new()
        {
            RawText = text,
            Code = match.Groups["code"].Value,
            SubCode = match.Groups["subCode"].Value,
            RedirectUrl = match.Groups["url"].Value,
            Message = match.Groups["message"].Value
        };
    }
}
