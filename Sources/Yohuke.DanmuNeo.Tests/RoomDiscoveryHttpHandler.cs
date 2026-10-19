using System.Threading;
using System.Net;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>为房间发现提供按地址分发的离线响应。</summary>
public class RoomDiscoveryHttpHandler : HttpMessageHandler
{
    private readonly object gate = new();

    /// <summary>收到的地址和凭据头。</summary>
    public List<(Uri Uri, string Cookie)> Requests { get; } = [];

    /// <summary>按地址生成响应。</summary>
    public Func<Uri, CancellationToken, Task<string>> RespondAsync { get; set; } =
        (_, _) => Task.FromResult("{\"code\":0,\"data\":{}}");

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var cookie = request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : "";

        lock (gate)
        {
            Requests.Add((uri, cookie));
        }

        var json = await RespondAsync(uri, cancellationToken);
        return new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
