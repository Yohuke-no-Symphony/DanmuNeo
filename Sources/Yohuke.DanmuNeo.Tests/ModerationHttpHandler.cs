using System.Net;
using System.Threading;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 禁言服务的离线 HTTP 响应与请求记录。
/// </summary>
public class ModerationHttpHandler : HttpMessageHandler
{
    /// <summary>
    /// 按请求顺序提供的响应。
    /// </summary>
    public Queue<string> Responses { get; } = new();

    /// <summary>
    /// 请求地址记录。
    /// </summary>
    public List<Uri> Requests { get; } = [];

    /// <summary>
    /// 各次请求携带的 Cookie。
    /// </summary>
    public List<string> RequestCookies { get; } = [];

    /// <summary>各次请求的原始表单。</summary>
    public List<string> Forms { get; } = [];

    /// <summary>各次请求的方法。</summary>
    public List<HttpMethod> Methods { get; } = [];

    /// <summary>
    /// 最后请求的表单。
    /// </summary>
    public string Form { get; private set; } = "";

    /// <summary>
    /// 最后请求的解码表单字段。
    /// </summary>
    public IReadOnlyDictionary<string, string> FormParameters => Form
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(parameter => parameter.Split('=', 2))
        .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));

    /// <summary>
    /// 最后请求的 HTTP 方法。
    /// </summary>
    public HttpMethod? Method { get; private set; }

    /// <summary>
    /// 最后请求的内容类型。
    /// </summary>
    public string? ContentType { get; private set; }

    /// <summary>
    /// 最后请求的 Cookie。
    /// </summary>
    public string Cookie { get; private set; } = "";

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        Method = request.Method;
        Methods.Add(request.Method);
        ContentType = request.Content?.Headers.ContentType?.MediaType;
        Cookie = request.Headers.TryGetValues("Cookie", out var values) ? string.Join(";", values) : "";
        RequestCookies.Add(Cookie);
        Form = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Forms.Add(Form);
        return new(HttpStatusCode.OK) { Content = new StringContent(Responses.Dequeue()) };
    }
}
