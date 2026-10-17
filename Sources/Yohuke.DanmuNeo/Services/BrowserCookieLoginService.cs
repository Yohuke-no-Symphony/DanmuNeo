using Yohuke.DanmuNeo.I18n;
using System.Net;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.BrowserLogin;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 从内嵌浏览器 Cookie 中提取平台登录信息。
/// </summary>
public class BrowserCookieLoginService
{
    /// <summary>
    /// B 站登录地址。
    /// </summary>
    public static readonly Uri BILIBILI_LOGIN_URI = new("https://passport.bilibili.com/login");

    /// <summary>
    /// QQ 音乐登录地址。
    /// </summary>
    public static readonly Uri QQ_MUSIC_LOGIN_URI = new("https://y.qq.com/");

    /// <summary>
    /// 读取平台登录起始地址。
    /// </summary>
    public Uri GetLoginUri(BrowserLoginPlatform platform)
    {
        return platform switch
        {
            BrowserLoginPlatform.Bilibili => BILIBILI_LOGIN_URI,
            BrowserLoginPlatform.QQMusic => QQ_MUSIC_LOGIN_URI,
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
        };
    }

    /// <summary>
    /// 读取平台显示名称。
    /// </summary>
    public string GetPlatformName(BrowserLoginPlatform platform)
    {
        return platform switch
        {
            BrowserLoginPlatform.Bilibili => Localization.Get("Services.BrowserLogin.Bilibili"),
            BrowserLoginPlatform.QQMusic => Localization.Get("Services.BrowserLogin.QqMusic"),
            _ => Localization.Get("Services.BrowserLogin.UnknownPlatform")
        };
    }

    /// <summary>
    /// 从浏览器 Cookie 创建登录结果。
    /// </summary>
    public BrowserCookieLoginResult CreateResult(BrowserLoginPlatform platform, IEnumerable<Cookie> cookies)
    {
        return platform switch
        {
            BrowserLoginPlatform.Bilibili => CreateBilibiliResult(cookies),
            BrowserLoginPlatform.QQMusic => CreateQQMusicResult(cookies),
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
        };
    }

    /// <summary>
    /// 读取浏览器登录 Profile 目录。
    /// </summary>
    public string GetProfileDirectory(BrowserLoginPlatform platform)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Yohuke.DanmuNeo",
            "WebLoginProfiles",
            platform.ToString());
    }

    private static BrowserCookieLoginResult CreateBilibiliResult(IEnumerable<Cookie> cookies)
    {
        var values = ToDictionary(cookies);
        values.TryGetValue("buvid3", out var buvid3);
        values.TryGetValue("SESSDATA", out var sessData);
        values.TryGetValue("bili_jct", out var biliJct);
        values.TryGetValue("DedeUserId", out var dedeUserId);
        values.TryGetValue("DedeUserID", out var dedeUserIdUpper);

        var cookie = ApiCookieHelper.CreateBilibiliCookie(
            $"buvid3={buvid3};SESSDATA={sessData};bili_jct={biliJct};DedeUserId={dedeUserId ?? dedeUserIdUpper}");

        if (string.IsNullOrWhiteSpace(sessData) || string.IsNullOrWhiteSpace(biliJct))
        {
            throw LocalizedExceptionText.Create("Services.BrowserLogin.BilibiliCookiesMissing",
                message => new InvalidOperationException(message));
        }

        return new()
        {
            Platform = BrowserLoginPlatform.Bilibili,
            Cookie = cookie,
            Message = Localization.Get("Services.BrowserLogin.BilibiliCookiesRead"),
            MessageKey = "Services.BrowserLogin.BilibiliCookiesRead"
        };
    }

    private static BrowserCookieLoginResult CreateQQMusicResult(IEnumerable<Cookie> cookies)
    {
        var values = ToDictionary(cookies);
        values.TryGetValue("uin", out var uin);
        values.TryGetValue("qm_keyst", out var qmKeyst);

        if (string.IsNullOrWhiteSpace(qmKeyst))
        {
            values.TryGetValue("qqmusic_key", out qmKeyst);
        }

        if (string.IsNullOrWhiteSpace(uin) || string.IsNullOrWhiteSpace(qmKeyst))
        {
            throw LocalizedExceptionText.Create("Services.BrowserLogin.QqMusicCookiesMissing",
                message => new InvalidOperationException(message));
        }

        return new()
        {
            Platform = BrowserLoginPlatform.QQMusic,
            Cookie = $"uin={uin};qm_keyst={qmKeyst}",
            Message = Localization.Get("Services.BrowserLogin.QqMusicCookiesRead"),
            MessageKey = "Services.BrowserLogin.QqMusicCookiesRead"
        };
    }

    private static Dictionary<string, string> ToDictionary(IEnumerable<Cookie> cookies)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var cookie in cookies)
        {
            if (!string.IsNullOrWhiteSpace(cookie.Value))
            {
                values[cookie.Name] = cookie.Value;
            }
        }

        return values;
    }
}
