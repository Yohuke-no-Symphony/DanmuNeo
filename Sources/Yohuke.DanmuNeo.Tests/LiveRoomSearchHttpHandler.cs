using System.Net;
using System.Threading;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 为搜索接口测试提供离线 HTTP 响应。
/// </summary>
public class LiveRoomSearchHttpHandler : HttpMessageHandler
{
    /// <summary>
    /// 返回的 JSON。
    /// </summary>
    public string ResponseJson { get; set; } = "";

    /// <summary>
    /// 最后请求的地址。
    /// </summary>
    public Uri? RequestUri { get; private set; }

    /// <summary>
    /// 最后请求的 Cookie。
    /// </summary>
    public string Cookie { get; private set; } = "";

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestUri = request.RequestUri;
        Cookie = request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : "";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ResponseJson)
        });
    }
}
