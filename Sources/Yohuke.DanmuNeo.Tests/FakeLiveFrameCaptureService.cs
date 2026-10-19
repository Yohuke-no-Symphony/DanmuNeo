using System.Threading;
using Yohuke.DanmuNeo.Models.Captures;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>绕过原生网页执行而返回离线 PNG 的会话边界测试服务。</summary>
internal sealed class FakeLiveFrameCaptureService(LiveCapturedFrame frame) : ILiveFrameCaptureService
{
    public event EventHandler<LiveCaptureShortcutEventArgs>? CaptureShortcutRequested;
    internal TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<LiveCapturedFrame>? DeferredFrame { get; init; }
    internal int RequestCount { get; private set; }

    /// <inheritdoc/>
    public Task<LiveCapturedFrame> RequestFrameAsync(string token, Func<string, Task> invokeScript,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestCount++;
        RequestStarted.TrySetResult();
        return DeferredFrame?.Task ?? Task.FromResult(frame);
    }

    internal void RequestShortcut(string token)
    {
        CaptureShortcutRequested?.Invoke(this, new(token));
    }
}
