namespace Yohuke.DanmuNeo.Models.BrowserLogin;

/// <summary>
/// 浏览器 Cookie 登录结果。
/// </summary>
public class BrowserCookieLoginResult
{
    /// <summary>
    /// 登录平台。
    /// </summary>
    public BrowserLoginPlatform Platform { get; set; }

    /// <summary>
    /// 标准化 Cookie。
    /// </summary>
    public string Cookie { get; set; } = "";

    /// <summary>
    /// 状态提示。
    /// </summary>
    public string Message { get; set; } = "";

    internal string? MessageKey { get; init; }
}
