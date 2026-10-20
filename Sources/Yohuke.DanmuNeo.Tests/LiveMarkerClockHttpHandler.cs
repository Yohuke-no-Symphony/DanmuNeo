using System.Net.Http;
using System.Threading;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 为直播记录时钟提供可取消的离线 HTTP 响应。
/// </summary>
public sealed class LiveMarkerClockHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;

    /// <summary>
    /// 初始化离线响应处理器。
    /// </summary>
    public LiveMarkerClockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        this.respond = respond;
    }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return respond(request, cancellationToken);
    }
}
