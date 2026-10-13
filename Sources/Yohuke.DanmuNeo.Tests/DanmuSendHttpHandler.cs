using System.Net;
using System.Threading;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 为发送服务提供离线 HTTP 响应与传输错误。
/// </summary>
public sealed class DanmuSendHttpHandler : HttpMessageHandler
{
    /// <summary>
    /// 返回的响应正文。
    /// </summary>
    public string ResponseJson { get; set; } = "";

    /// <summary>
    /// HTTP 状态。
    /// </summary>
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    /// <summary>
    /// 请求时抛出的传输异常。
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// 实际请求次数。
    /// </summary>
    public int RequestCount { get; private set; }

    /// <summary>
    /// 各次发送的解码表单。
    /// </summary>
    public List<IReadOnlyDictionary<string, string>> Forms { get; } = [];

    /// <summary>
    /// 各次发送携带的 Cookie。
    /// </summary>
    public List<string> Cookies { get; } = [];

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;

        if (Exception is not null)
        {
            throw Exception;
        }

        Cookies.Add(request.Headers.TryGetValues("Cookie", out var values) ? string.Join(";", values) : "");
        var form = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Forms.Add(form.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter => parameter.Split('=', 2))
            .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1])));
        return new(StatusCode) { Content = new StringContent(ResponseJson) };
    }
}
