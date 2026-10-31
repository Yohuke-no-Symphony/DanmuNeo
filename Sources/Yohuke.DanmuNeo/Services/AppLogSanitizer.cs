using System.Text.RegularExpressions;

namespace Yohuke.DanmuNeo.Services;

internal static class AppLogSanitizer
{
    private const string REDACTED = "[已隐藏]";
    private static readonly Regex URL = new(@"\b(?:https?|wss?|rtmps?)://[^\s<>""']+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HEADERS = new(@"\b(?:set-cookie|cookie|proxy-authorization|authorization)\s*[:=]\s*[^\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CREDENTIALS = new(
        @"(?<key>[""']?(?:SESSDATA|bili_jct|DedeUserID(?:__ckMd5)?|csrf(?:_token)?|access_token|refresh_token|token|auth_key|api_key|password|passwd|buvid[34]|buvid_fp|ac_time_value|cookie|authorization)[""']?\s*[:=]\s*)(?:\[已隐藏\]|""[^""]*""|'[^']*'|[^\s,;}\]]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BEARER = new(@"\bBearer\s+[A-Za-z0-9._~+/-]+=*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LOCAL_SESSION_PATH = new(@"^/(?<route>stream|diagnostic)/[^/?#]+",
        RegexOptions.Compiled);

    internal static string Sanitize(string text, IEnumerable<string>? sensitiveValues = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var sanitized = text.Replace(@"\/", "/", StringComparison.Ordinal);
        sanitized = URL.Replace(sanitized, SanitizeUrl);
        sanitized = HEADERS.Replace(sanitized, match => match.Value[..(match.Value.IndexOfAny([':', '=']) + 1)] + " " + REDACTED);
        sanitized = CREDENTIALS.Replace(sanitized, match => match.Groups["key"].Value + REDACTED);
        sanitized = BEARER.Replace(sanitized, "Bearer " + REDACTED);
        if (sensitiveValues is not null)
        {
            foreach (var value in sensitiveValues.Where(value => !string.IsNullOrEmpty(value))
                         .Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length))
            {
                sanitized = sanitized.Replace(value, REDACTED, StringComparison.Ordinal);
            }
        }

        return sanitized;
    }

    private static string SanitizeUrl(Match match)
    {
        var value = match.Value;
        var queryIndex = value.IndexOfAny(['?', '#']);

        if (queryIndex >= 0)
        {
            value = value[..queryIndex] + "?" + REDACTED;
        }

        var authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = value.IndexOf('/', authorityStart);

        if (authorityEnd >= 0 && Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) && uri.IsLoopback)
        {
            value = value[..authorityEnd] + LOCAL_SESSION_PATH.Replace(value[authorityEnd..],
                path => "/" + path.Groups["route"].Value + "/" + REDACTED);
        }

        var userInfoEnd = value.IndexOf('@', authorityStart,
            (authorityEnd >= 0 ? authorityEnd : value.Length) - authorityStart);

        if (userInfoEnd >= 0)
        {
            value = value[..authorityStart] + REDACTED + "@" + value[(userInfoEnd + 1)..];
        }

        return value;
    }
}
