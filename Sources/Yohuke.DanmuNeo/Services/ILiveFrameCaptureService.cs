using Yohuke.DanmuNeo.Models.Captures;

namespace Yohuke.DanmuNeo.Services;

/// <summary>通过当前页面抓取视频原帧，并接收原生网页焦点下的截图动作。</summary>
public interface ILiveFrameCaptureService
{
    event EventHandler<LiveCaptureShortcutEventArgs>? CaptureShortcutRequested;

    /// <summary>先注册请求，再调用页面脚本；超时、撤销与取消均使请求失效。</summary>
    Task<LiveCapturedFrame> RequestFrameAsync(string token, Func<string, Task> invokeScript,
        CancellationToken cancellationToken = default);
}
