using System.Net;
using System.Threading;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 为 GitHub 更新检查提供离线响应和请求记录。
/// </summary>
public sealed class AppUpdateHttpHandler : HttpMessageHandler
{
    public string ResponseJson { get; set; } = "null";
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public bool WaitForCancellation { get; set; }
    public Uri? RequestUri { get; private set; }
    public string UserAgent { get; private set; } = "";
    public string Accept { get; private set; } = "";
    public bool HasCredentials { get; private set; }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestUri = request.RequestUri;
        UserAgent = request.Headers.UserAgent.ToString();
        Accept = request.Headers.Accept.ToString();
        HasCredentials = request.Headers.Contains("Cookie") || request.Headers.Contains("Authorization");

        if (WaitForCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return new(StatusCode) { Content = new StringContent(ResponseJson) };
    }
}