using System.Net;
using System.Threading;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 为弹幕配置提供离线响应，并记录各次请求的身份和表单。
/// </summary>
public sealed class DanmuConfigHttpHandler : HttpMessageHandler
{
    /// <summary>
    /// 按请求顺序返回的 JSON。
    /// </summary>
    public Queue<string> Responses { get; } = new();

    /// <summary>
    /// 请求地址。
    /// </summary>
    public List<Uri> Requests { get; } = [];

    /// <summary>
    /// 每次请求使用的 Cookie。
    /// </summary>
    public List<string> Cookies { get; } = [];

    /// <summary>
    /// 每次请求的解码表单。
    /// </summary>
    public List<IReadOnlyDictionary<string, string>> Forms { get; } = [];

    /// <summary>
    /// 请求记录完成后、响应返回前的控制点。
    /// </summary>
    public Func<HttpRequestMessage, CancellationToken, Task>? BeforeResponseAsync { get; set; }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        Cookies.Add(request.Headers.TryGetValues("Cookie", out var values) ? string.Join(";", values) : "");
        var form = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Forms.Add(form.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter => parameter.Split('=', 2))
            .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1])));

        if (BeforeResponseAsync is not null)
        {
            await BeforeResponseAsync(request, cancellationToken);
        }

        return new(HttpStatusCode.OK) { Content = new StringContent(Responses.Dequeue()) };
    }
}
