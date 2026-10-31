using Yohuke.DanmuNeo.Apis;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证登录和发送共同使用的 B 站 Cookie 规范化边界。
/// </summary>
public class ApiCookieHelperTests
{
    /// <summary>
    /// 已有设备标识与会话一起保留，名称大小写不会改变提取结果。
    /// </summary>
    [Fact]
    public void CreateBilibiliCookie_PreservesExistingDeviceCookies()
    {
        var cookie = ApiCookieHelper.CreateBilibiliCookie(
            "BUVID3=device3;SESSDATA=session;bili_jct=csrf;DedeUserID=42;BUVID4=device4%3D;BUVID_FP=fingerprint");

        Assert.Equal(
            "buvid3=device3;SESSDATA=session;bili_jct=csrf;DedeUserId=42;buvid4=device4%3D;buvid_fp=fingerprint",
            cookie);
    }

    /// <summary>
    /// 不存在或为空的附加设备 Cookie 不会改变旧版四字段格式。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(";buvid4=;buvid_fp=")]
    [InlineData(";buvid4= ;buvid_fp= ")]
    public void CreateBilibiliCookie_KeepsLegacyFormatWhenDeviceCookiesAreMissing(string suffix)
    {
        var cookie = ApiCookieHelper.CreateBilibiliCookie(
            "buvid3=device3;SESSDATA=session;bili_jct=csrf;DedeUserID=42" + suffix);

        Assert.Equal("buvid3=device3;SESSDATA=session;bili_jct=csrf;DedeUserId=42", cookie);
    }

    /// <summary>
    /// 仅保留允许的 B 站凭据及设备字段，不扩大发送 Cookie 的范围。
    /// </summary>
    [Fact]
    public void CreateBilibiliCookie_ExcludesUnrelatedCookies()
    {
        var cookie = ApiCookieHelper.CreateBilibiliCookie(
            "SESSDATA=session;bili_jct=csrf;DedeUserID=42;buvid4=device4;buvid_fp=fingerprint;" +
            "PVID=tracking;_uuid=other;qm_keyst=qq-secret;unknown=secret");

        Assert.Equal(
            "buvid3=;SESSDATA=session;bili_jct=csrf;DedeUserId=42;buvid4=device4;buvid_fp=fingerprint",
            cookie);
    }

    /// <summary>
    /// 重复规范化不会移除设备字段或重复追加字段。
    /// </summary>
    [Fact]
    public void CreateBilibiliCookie_IsIdempotentWithDeviceCookies()
    {
        var cookie = "buvid3=device3;SESSDATA=session;bili_jct=csrf;DedeUserId=42;buvid4=device4;buvid_fp=fingerprint";

        Assert.Equal(cookie, ApiCookieHelper.CreateBilibiliCookie(ApiCookieHelper.CreateBilibiliCookie(cookie)));
    }
}
