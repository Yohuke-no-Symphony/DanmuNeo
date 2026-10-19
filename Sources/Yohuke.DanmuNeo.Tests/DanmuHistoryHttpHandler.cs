using System.Threading;
using System.Net;
using System.Text;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class DanmuHistoryHttpHandler(string body) : HttpMessageHandler
{
    internal string RequestUri { get; private set; } = "";
    internal string Cookie { get; private set; } = "";

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUri = request.RequestUri!.ToString();
        Cookie = request.Headers.TryGetValues("Cookie", out var values) ? string.Join(";", values) : "";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}
