using Yohuke.DanmuNeo.Models.Captures;

namespace Yohuke.DanmuNeo.Services;

/// <summary>与播放会话及一次抓图请求绑定的完成边界。</summary>
internal sealed class LiveFrameCaptureRequest
{
    public required string Token { get; init; }
    public required LivePlayerStreamEntry Stream { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public bool IsReceiving { get; set; }
    public TaskCompletionSource<LiveCapturedFrame> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
