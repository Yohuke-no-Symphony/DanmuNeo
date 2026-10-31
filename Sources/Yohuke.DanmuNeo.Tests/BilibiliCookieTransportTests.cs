using Yohuke.DanmuNeo.Apis;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证设备 Cookie 随实际发送账号传递到 HTTP 请求。
/// </summary>
public class BilibiliCookieTransportTests
{
    /// <summary>
    /// 多账号发送与 Cookie 更新使用各自设备字段，不保留上个账号的数据。
    /// </summary>
    [Fact]
    public async Task SendDanmuAsync_PreservesDeviceCookiesPerAccountAndClearsRemovedValues()
    {
        using var handler = new DanmuSendHttpHandler { ResponseJson = "{\"code\":0,\"data\":{}}" };
        using var client = new HttpClient(handler);
        using var api = new BilibiliApi(
        [
            "SESSDATA=session-a;bili_jct=csrf-a;DedeUserID=1;buvid4=device-a;buvid_fp=fingerprint-a",
            "SESSDATA=session-b;bili_jct=csrf-b;DedeUserID=2;buvid4=device-b;buvid_fp=fingerprint-b"
        ], httpClient: client);

        await api.SendDanmuAsync(100, "第一条", number: 0);
        await api.SendDanmuAsync(100, "第二条", number: 1);
        api.UpdateCookie("SESSDATA=session-c;bili_jct=csrf-c;DedeUserID=3", 0);
        await api.SendDanmuAsync(100, "第三条", number: 0);

        Assert.Equal(3, handler.RequestCount);
        Assert.Equal("buvid3=;SESSDATA=session-a;bili_jct=csrf-a;DedeUserId=1;buvid4=device-a;buvid_fp=fingerprint-a", handler.Cookies[0]);
        Assert.Equal("buvid3=;SESSDATA=session-b;bili_jct=csrf-b;DedeUserId=2;buvid4=device-b;buvid_fp=fingerprint-b", handler.Cookies[1]);
        Assert.Equal("buvid3=;SESSDATA=session-c;bili_jct=csrf-c;DedeUserId=3", handler.Cookies[2]);
        Assert.Equal(["csrf-a", "csrf-b", "csrf-c"], handler.Forms.Select(form => form["csrf"]).ToArray());
        Assert.Equal(["csrf-a", "csrf-b", "csrf-c"], handler.Forms.Select(form => form["csrf_token"]).ToArray());
    }
}
