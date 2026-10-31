using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Yohuke.DanmuNeo.Apis;

namespace Yohuke.DanmuNeo.Services;

/// <summary>一次实际发送的请求摘要与脱敏响应，不保存正文或账号凭据。</summary>
public sealed record DanmuSendDiagnostic
{
    internal const int MAX_RESPONSE_TEXT_LENGTH = 1024;
    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>实际请求的公开直播间号。</summary>
    public long RoomId { get; init; }

    /// <summary>实际发送文本的 UTF-16 长度。</summary>
    public int TextLength { get; init; }

    /// <summary>实际发送文本按 UTF-8 计算的 SHA-256，用于比较请求内容。</summary>
    public string TextSha256 { get; init; } = "";

    /// <summary>实际请求的弹幕颜色。</summary>
    public int Color { get; init; }

    /// <summary>实际请求的弹幕模式。</summary>
    public int Mode { get; init; }

    /// <summary>实际 API 实例使用的 rnd 参数。</summary>
    public int Rnd { get; init; }

    /// <summary>实际请求中有值的受支持 Cookie 字段名称，不含值。</summary>
    public IReadOnlyList<string> CookieFields { get; init; } = [];

    /// <summary>接口业务码，未收到可解析响应时为空。</summary>
    [JsonPropertyName("code")]
    public int? ApiCode { get; init; }

    /// <summary>独立保留的 message 字段，已脱敏并限制长度。</summary>
    [JsonPropertyName("message")]
    public string? ResponseMessage { get; init; }

    /// <summary>独立保留的 msg 字段，已脱敏并限制长度。</summary>
    [JsonPropertyName("msg")]
    public string? ResponseMsg { get; init; }

    /// <summary>HTTP 异常明确提供的状态码，其他情况为空。</summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>生成逐字段日志详情，响应字段以单行 JSON 标量区分空值并转义换行。</summary>
    public string ToLogDetails()
    {
        return string.Join(Environment.NewLine,
            JsonSerializer.SerializeToElement(this, JSON_OPTIONS).EnumerateObject()
                .Select(property => $"{property.Name}={property.Value.GetRawText()}"));
    }

    internal static DanmuSendDiagnostic Create(long roomId, string text, string cookie, int color, int mode, int rnd)
    {
        return new()
        {
            RoomId = roomId,
            TextLength = text.Length,
            TextSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
            Color = color,
            Mode = mode,
            Rnd = rnd,
            CookieFields = ApiCookieHelper.Parse(ApiCookieHelper.CreateBilibiliCookie(cookie))
                .Where(pair => !string.IsNullOrEmpty(pair.Value))
                .Select(pair => pair.Key)
                .Order(StringComparer.Ordinal)
                .ToArray()
        };
    }

    internal DanmuSendDiagnostic WithResponse(int code, string? message, string? msg, string cookie)
    {
        var values = ApiCookieHelper.Parse(cookie).Values;
        return this with
        {
            ApiCode = code,
            ResponseMessage = SanitizeResponse(message, values),
            ResponseMsg = SanitizeResponse(msg, values)
        };
    }

    private static string? SanitizeResponse(string? text, IEnumerable<string> sensitiveValues)
    {
        if (text is null)
        {
            return null;
        }

        var sanitized = AppLogSanitizer.Sanitize(text, sensitiveValues);
        return sanitized.Length <= MAX_RESPONSE_TEXT_LENGTH
            ? sanitized
            : sanitized[..MAX_RESPONSE_TEXT_LENGTH] + "…";
    }
}
