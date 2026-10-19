using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>执行一次弹幕发送，不包含重试、排队或历史。</summary>
public interface IDanmuSendTransport
{
    /// <summary>使用指定身份和配置执行一次请求。</summary>
    Task<DanmuSendAttemptResult> SendOnceAsync(string roomId, string message, string cookie,
        DanmuRoomConfig config, TimeSpan timeout, CancellationToken cancellationToken);
}
