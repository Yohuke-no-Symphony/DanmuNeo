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

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;

        if (Exception is not null)
        {
            return Task.FromException<HttpResponseMessage>(Exception);
        }

        return Task.FromResult(new HttpResponseMessage(StatusCode) { Content = new StringContent(ResponseJson) });
    }
}
