using System.Text;
using System.Threading;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证表情缓存的落盘位置、并发下载和取消边界。
/// </summary>
public sealed class EmoticonCacheServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 并发请求共享一次下载，重建服务后直接使用磁盘缓存。
    /// </summary>
    [Fact]
    public async Task ConcurrentRequestsShareDownloadAndReuseDiskCache()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new EmoticonHttpHandler
        {
            BeforeResponseAsync = async (request, token) =>
            {
                Assert.Null(request.Headers.Referrer);
                Assert.False(request.Headers.Contains("Cookie"));
                await release.Task.WaitAsync(token);
            }
        };
        using var client = new HttpClient(handler);
        var directories = new AppDirectoryService(directory);
        string path;

        using (var service = new EmoticonCacheService(directories, client))
        {
            var first = service.GetEmoticonAsync("https://i0.hdslb.com/test.png");
            var second = service.GetEmoticonAsync("https://i0.hdslb.com/test.png");
            Assert.Equal(1, handler.RequestCount);
            release.SetResult();
            var paths = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(paths[0], paths[1]);
            path = paths[0];
        }

        Assert.Equal(Path.Combine(directory, "temp", "emots"), Path.GetDirectoryName(path));
        Assert.Equal(EmoticonHttpHandler.ImageBytes, await File.ReadAllBytesAsync(path));
        using var restoredService = new EmoticonCacheService(directories, client);
        Assert.Equal(path, await restoredService.GetEmoticonAsync("https://i0.hdslb.com/test.png"));
        Assert.Equal(1, handler.RequestCount);
        Assert.Single(Directory.EnumerateFiles(directories.EmoticonCacheDirectory));
    }

    /// <summary>
    /// 取消一个弹幕的等待不会中断其他直播间共享的图片下载。
    /// </summary>
    [Fact]
    public async Task CancellingOneWaiterKeepsSharedDownloadAlive()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new EmoticonHttpHandler
        {
            BeforeResponseAsync = (_, token) => release.Task.WaitAsync(token)
        };
        using var client = new HttpClient(handler);
        using var service = new EmoticonCacheService(new(directory), client);
        using var cancellation = new CancellationTokenSource();
        var cancelled = service.GetEmoticonAsync("https://i0.hdslb.com/shared.png", cancellation.Token);
        var remaining = service.GetEmoticonAsync("https://i0.hdslb.com/shared.png");

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        release.SetResult();

        Assert.True(File.Exists(await remaining.WaitAsync(TimeSpan.FromSeconds(3))));
        Assert.Equal(1, handler.RequestCount);
    }

    /// <summary>
    /// 错误页面不污染缓存，后续请求仍能重新下载图片。
    /// </summary>
    [Fact]
    public async Task NonImageResponseIsNotCachedAndCanRetry()
    {
        var handler = new EmoticonHttpHandler { ResponseBytes = Encoding.UTF8.GetBytes("<html>unavailable</html>") };
        using var client = new HttpClient(handler);
        var directories = new AppDirectoryService(directory);
        using var service = new EmoticonCacheService(directories, client);

        Assert.Empty(await service.GetEmoticonAsync("https://i0.hdslb.com/retry.png"));
        Assert.False(Directory.Exists(directories.EmoticonCacheDirectory));
        handler.ResponseBytes = EmoticonHttpHandler.ImageBytes;

        Assert.True(File.Exists(await service.GetEmoticonAsync("https://i0.hdslb.com/retry.png")));
        Assert.Equal(2, handler.RequestCount);
        Assert.Empty(Directory.EnumerateFiles(directories.EmoticonCacheDirectory, "*.tmp"));
    }

    /// <summary>
    /// 关闭服务会结束正在等待响应的下载，不产生残留缓存。
    /// </summary>
    [Fact]
    public async Task DisposeCancelsPendingDownload()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new EmoticonHttpHandler
        {
            BeforeResponseAsync = async (_, token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        };
        using var client = new HttpClient(handler);
        var directories = new AppDirectoryService(directory);
        using var service = new EmoticonCacheService(directories, client);
        var download = service.GetEmoticonAsync("https://i0.hdslb.com/pending.png");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        service.Dispose();

        Assert.Empty(await download.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.False(Directory.Exists(directories.EmoticonCacheDirectory));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }

        GC.SuppressFinalize(this);
    }
}
