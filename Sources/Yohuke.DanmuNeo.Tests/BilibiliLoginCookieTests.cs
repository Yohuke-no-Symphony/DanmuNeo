using System.Net;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证二维码登录与 WebSocket 共用的 Cookie 提取和序列化。
/// </summary>
public class BilibiliLoginCookieTests
{
    /// <summary>
    /// 登录 Cookie 模型序列化时保留已有设备字段。
    /// </summary>
    [Fact]
    public void ToCookieString_PreservesExistingDeviceCookies()
    {
        var cookie = new BilibiliLoginCookie
        {
            Buvid3 = "device3",
            Buvid4 = "device4",
            BuvidFp = "fingerprint",
            SessData = "session",
            BiliJct = "csrf",
            DedeUserId = "42"
        };

        Assert.Equal(
            "buvid3=device3;SESSDATA=session;bili_jct=csrf;DedeUserId=42;buvid4=device4;buvid_fp=fingerprint",
            cookie.ToCookieString());
    }

    /// <summary>
    /// 旧版登录结果不会补造任何额外设备字段。
    /// </summary>
    [Fact]
    public void ToCookieString_KeepsLegacyFormatWithoutDeviceCookies()
    {
        var cookie = new BilibiliLoginCookie
        {
            Buvid3 = "device3",
            SessData = "session",
            BiliJct = "csrf",
            DedeUserId = "42"
        };

        Assert.Equal("buvid3=device3;SESSDATA=session;bili_jct=csrf;DedeUserId=42", cookie.ToCookieString());
    }

    /// <summary>
    /// WebSocket 使用的字符串提取入口保留设备字段，并过滤非白名单字段。
    /// </summary>
    [Fact]
    public void FromCookieString_RoundTripsAllowedCookies()
    {
        var cookie = BilibiliLoginCookie.FromCookieString(
            "BUVID3=device3;SESSDATA=session;bili_jct=csrf;DedeUserID=42;" +
            "BUVID4=device4;BUVID_FP=fingerprint;unknown=other");

        Assert.Equal("device3", cookie.Buvid3);
        Assert.Equal("device4", cookie.Buvid4);
        Assert.Equal("fingerprint", cookie.BuvidFp);
        Assert.Equal("session", cookie.SessData);
        Assert.Equal("csrf", cookie.BiliJct);
        Assert.Equal("42", cookie.DedeUserId);
        Assert.Equal(
            "buvid3=device3;SESSDATA=session;bili_jct=csrf;DedeUserId=42;buvid4=device4;buvid_fp=fingerprint",
            cookie.ToCookieString());
    }

    /// <summary>
    /// 二维码回跳 Cookie 容器仅提取 B 站域名的数据，保留已有设备信息。
    /// </summary>
    [Fact]
    public void FromCookies_ExtractsOnlyBilibiliCookiesFromRedirectContainer()
    {
        var container = new CookieContainer();
        container.Add(new Cookie("buvid3", "device3", "/", ".bilibili.com"));
        container.Add(new Cookie("buvid4", "device4", "/", ".bilibili.com"));
        container.Add(new Cookie("buvid_fp", "fingerprint", "/", "passport.bilibili.com"));
        container.Add(new Cookie("SESSDATA", "session", "/", ".bilibili.com"));
        container.Add(new Cookie("bili_jct", "csrf", "/", ".bilibili.com"));
        container.Add(new Cookie("DedeUserID", "42", "/", ".bilibili.com"));
        container.Add(new Cookie("SESSDATA", "other-session", "/", ".qq.com"));
        container.Add(new Cookie("buvid4", "other-device", "/", "bilibili.com.example.com"));

        var cookie = BilibiliLoginCookie.FromCookies(container.GetAllCookies().Cast<Cookie>());

        Assert.Equal(
            "buvid3=device3;SESSDATA=session;bili_jct=csrf;DedeUserId=42;buvid4=device4;buvid_fp=fingerprint",
            cookie.ToCookieString());
    }

    /// <summary>
    /// B 站主域和子域的大小写差异不会丢失合法设备 Cookie。
    /// </summary>
    [Theory]
    [InlineData("bilibili.com")]
    [InlineData(".BILIBILI.COM")]
    [InlineData("live.bilibili.com")]
    public void FromCookies_AcceptsBilibiliDomains(string domain)
    {
        var cookie = BilibiliLoginCookie.FromCookies([new("buvid4", "device4", "/", domain)]);

        Assert.Equal("device4", cookie.Buvid4);
    }
}
