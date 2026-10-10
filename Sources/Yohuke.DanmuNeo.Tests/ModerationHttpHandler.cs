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
    /// 最后请求的表单。
    /// </summary>
    public string Form { get; private set; } = "";

    /// <summary>
    /// 最后请求的 Cookie。
    /// </summary>
    public string Cookie { get; private set; } = "";

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        Cookie = request.Headers.TryGetValues("Cookie", out var values) ? string.Join(";", values) : "";
        Form = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        return new(HttpStatusCode.OK) { Content = new StringContent(Responses.Dequeue()) };
    }
}
