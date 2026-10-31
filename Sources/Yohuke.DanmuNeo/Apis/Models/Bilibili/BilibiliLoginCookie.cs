using System.Net;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站登录 Cookie。
/// </summary>
public class BilibiliLoginCookie
{
    /// <summary>
    /// 浏览器设备标识。
    /// </summary>
    public string Buvid3 { get; set; } = "";

    /// <summary>
    /// 浏览器已有的附加设备标识。
    /// </summary>
    public string Buvid4 { get; set; } = "";

    /// <summary>
    /// 浏览器已有的设备指纹。
    /// </summary>
    public string BuvidFp { get; set; } = "";

    /// <summary>
    /// 登录会话。
    /// </summary>
    public string SessData { get; set; } = "";

    /// <summary>
    /// CSRF 令牌。
    /// </summary>
    public string BiliJct { get; set; } = "";

    /// <summary>
    /// 用户 ID。
    /// </summary>
    public string DedeUserId { get; set; } = "";

    /// <summary>
    /// 拼装为 B 站直播接口使用的 Cookie 字符串。
    /// </summary>
    public string ToCookieString()
    {
        return ApiCookieHelper.CreateBilibiliCookie(
            $"buvid3={Buvid3};SESSDATA={SessData};bili_jct={BiliJct};DedeUserId={DedeUserId};buvid4={Buvid4};buvid_fp={BuvidFp}");
    }

    internal static BilibiliLoginCookie FromCookieString(string cookie)
    {
        var values = ApiCookieHelper.Parse(cookie);
        return new()
        {
            Buvid3 = values.GetValueOrDefault("buvid3", ""),
            Buvid4 = values.GetValueOrDefault("buvid4", ""),
            BuvidFp = values.GetValueOrDefault("buvid_fp", ""),
            SessData = values.GetValueOrDefault("SESSDATA", ""),
            BiliJct = values.GetValueOrDefault("bili_jct", ""),
            DedeUserId = values.GetValueOrDefault("DedeUserId", "")
        };
    }

    internal static BilibiliLoginCookie FromCookies(IEnumerable<Cookie> cookies)
    {
        return FromCookieString(ApiCookieHelper.CreateBilibiliCookie(cookies));
    }
}
