using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.Logging;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 按图片地址共享下载任务，并将表情原图缓存到用户配置目录。
/// </summary>
public sealed class EmoticonCacheService : IEmoticonCacheService, IDisposable
{
    private const int MAX_IMAGE_BYTES = 8 * 1024 * 1024;
    private static readonly TimeSpan DOWNLOAD_TIMEOUT = TimeSpan.FromSeconds(15);
    private readonly AppDirectoryService directoryService;
    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;
    private readonly object syncRoot = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> downloads = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim downloadSlots = new(4);
    private readonly CancellationTokenSource lifetime = new();
    private bool isDisposed;

    /// <summary>
    /// 初始化表情缓存服务，可注入目录和 HTTP 客户端。
    /// </summary>
    public EmoticonCacheService(AppDirectoryService directoryService, HttpClient? httpClient = null)
    {
        this.directoryService = directoryService;
        this.httpClient = httpClient ?? new();
        disposeHttpClient = httpClient is null;
    }

    /// <inheritdoc/>
    public async Task<string> GetEmoticonAsync(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return "";
        }

        Task<string> task;

        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            var download = downloads.GetOrAdd(uri.AbsoluteUri,
                _ => new(() => DownloadAndRemoveAsync(uri), LazyThreadSafetyMode.ExecutionAndPublication));
            task = download.Value;
        }

        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Task<string>[] pendingDownloads;

        lock (syncRoot)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            lifetime.Cancel();
            pendingDownloads = downloads.Values.Where(download => download.IsValueCreated)
                .Select(download => download.Value).ToArray();
        }

        _ = DisposeAfterDownloadsAsync(pendingDownloads);
        GC.SuppressFinalize(this);
    }

    private async Task<string> DownloadAndRemoveAsync(Uri uri)
    {
        try
        {
            await downloadSlots.WaitAsync(lifetime.Token).ConfigureAwait(false);

            try
            {
                return await DownloadAsync(uri, lifetime.Token).ConfigureAwait(false);
            }
            finally
            {
                downloadSlots.Release();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            return "";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
                                           UnauthorizedAccessException or OperationCanceledException)
        {
            StartupLog.Append("表情图片下载失败", AppLogLevel.WARNING, "表情缓存", exception);
            return "";
        }
        finally
        {
            downloads.TryRemove(uri.AbsoluteUri, out _);
        }
    }

    private async Task<string> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant();
        var filePath = Path.Combine(directoryService.EmoticonCacheDirectory, $"{hash}.img");

        if (File.Exists(filePath))
        {
            var cachedBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

            if (IsSupportedImage(cachedBytes))
            {
                return filePath;
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DOWNLOAD_TIMEOUT);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", BaseApi.DEFAULT_USER_AGENT);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > MAX_IMAGE_BYTES)
        {
            return "";
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var imageStream = new MemoryStream();
        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await responseStream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (imageStream.Length + bytesRead > MAX_IMAGE_BYTES)
            {
                return "";
            }

            imageStream.Write(buffer, 0, bytesRead);
        }

        var bytes = imageStream.ToArray();

        if (!IsSupportedImage(bytes))
        {
            return "";
        }

        Directory.CreateDirectory(directoryService.EmoticonCacheDirectory);
        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            File.Move(temporaryPath, filePath, true);
            return filePath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsSupportedImage(ReadOnlySpan<byte> bytes)
    {
        return bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
               bytes.StartsWith(new byte[] { 255, 216, 255 }) ||
               bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8) ||
               (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..].StartsWith("WEBP"u8));
    }

    private async Task DisposeAfterDownloadsAsync(Task<string>[] pendingDownloads)
    {
        try
        {
            await Task.WhenAll(pendingDownloads).ConfigureAwait(false);
        }
        finally
        {
            if (disposeHttpClient)
            {
                httpClient.Dispose();
            }

            downloadSlots.Dispose();
            lifetime.Dispose();
        }
    }
}
