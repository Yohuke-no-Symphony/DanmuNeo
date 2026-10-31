using System.Net;
using System.Text.RegularExpressions;

namespace Yohuke.DanmuNeo.Apis;

/// <summary>
/// 提供平台 Cookie 的解析与写入能力。
/// </summary>
public static class ApiCookieHelper
{
    private static readonly string[] OPTIONAL_BILIBILI_COOKIE_NAMES = ["buvid4", "buvid_fp"];

    /// <summary>
    /// 从 Cookie 字符串解析键值。
    /// </summary>
    public static Dictionary<string, string> Parse(string cookie)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cleanedCookie = Regex.Replace(cookie, @"\s+", "");
        var segments = cleanedCookie.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var index = segment.IndexOf('=');

            if (index <= 0)
            {
                continue;
            }

            var key = segment[..index];
            var value = segment[(index + 1)..];
            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// 生成 B 站直播接口所需 Cookie。
    /// </summary>
    public static string CreateBilibiliCookie(string cookie)
    {
        var values = Parse(cookie);
        values.TryGetValue("buvid3", out var buvid3);
        values.TryGetValue("SESSDATA", out var sessData);
        values.TryGetValue("bili_jct", out var biliJct);
        values.TryGetValue("DedeUserId", out var dedeUserId);
        values.TryGetValue("DedeUserID", out var dedeUserIdUpper);

        var normalizedCookie = $"buvid3={buvid3 ?? ""};SESSDATA={sessData ?? ""};bili_jct={biliJct ?? ""};DedeUserId={dedeUserId ?? dedeUserIdUpper ?? ""}";

        foreach (var name in OPTIONAL_BILIBILI_COOKIE_NAMES)
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                normalizedCookie += $";{name}={value}";
            }
        }

        return normalizedCookie;
    }

    /// <summary>
    /// 从浏览器或登录回跳结果中提取 B 站 Cookie，排除其他域的同名字段。
    /// </summary>
    public static string CreateBilibiliCookie(IEnumerable<Cookie> cookies)
    {
        var values = cookies
            .Where(cookie => string.IsNullOrWhiteSpace(cookie.Domain) || IsBilibiliDomain(cookie.Domain))
            .Where(cookie => !string.IsNullOrWhiteSpace(cookie.Value))
            .Select(cookie => $"{cookie.Name}={cookie.Value}");
        return CreateBilibiliCookie(string.Join(";", values));
    }

    /// <summary>
    /// 将 Cookie 字符串写入容器。
    /// </summary>
    public static void AddCookies(CookieContainer container, Uri uri, string cookie)
    {
        foreach (var pair in Parse(cookie))
        {
            container.Add(uri, new Cookie(pair.Key, pair.Value));
        }
    }

    /// <summary>
    /// 从 Cookie 字符串中读取指定值。
    /// </summary>
    public static string GetValue(string cookie, string name)
    {
        var values = Parse(cookie);
        return values.TryGetValue(name, out var value) ? value : "";
    }

    private static bool IsBilibiliDomain(string domain)
    {
        var normalizedDomain = domain.TrimStart('.');
        return normalizedDomain.Equals("bilibili.com", StringComparison.OrdinalIgnoreCase)
               || normalizedDomain.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase);
    }
}
