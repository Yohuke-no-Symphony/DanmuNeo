using System.Net;
using System.Text;
using System.Threading;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class MedalHttpHandler(params string[] responses) : HttpMessageHandler
{
    private readonly Queue<string> responses = new(responses);
    internal List<string> Uris { get; } = [];
    internal List<string> Bodies { get; } = [];
    internal List<string> Cookies { get; } = [];

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uris.Add(request.RequestUri!.ToString());
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        Cookies.Add(request.Headers.TryGetValues("Cookie", out var values) ? string.Join(";", values) : "");
        return new(HttpStatusCode.OK) { Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json") };
    }
}
