using System.Net;
using System.Threading;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 为表情缓存提供可控制的离线图片响应。
/// </summary>
public sealed class EmoticonHttpHandler : HttpMessageHandler
{
    private int requestCount;

    /// <summary>
    /// 有效的单像素 PNG 样本。
    /// </summary>
    public static byte[] ImageBytes => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    /// <summary>
    /// 已发起的下载次数。
    /// </summary>
    public int RequestCount => Volatile.Read(ref requestCount);

    /// <summary>
    /// 需要返回的图片或错误正文。
    /// </summary>
    public byte[] ResponseBytes { get; set; } = ImageBytes;

    /// <summary>
    /// 返回响应前的控制点。
    /// </summary>
    public Func<HttpRequestMessage, CancellationToken, Task>? BeforeResponseAsync { get; set; }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref requestCount);

        if (BeforeResponseAsync is not null)
        {
            await BeforeResponseAsync(request, cancellationToken);
        }

        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(ResponseBytes) };
    }
}
