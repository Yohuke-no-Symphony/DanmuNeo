namespace Yohuke.DanmuNeo.Services;

/// <summary>直播间接收模块的应用级依赖。</summary>
public sealed class LiveRoomReceptionServices(IBilibiliDanmuHistoryService history,
    IReceivedDanmuArchiveService? archive = null, IBilibiliAccountSessionService? accountSessions = null)
{
    /// <summary>近期弹幕查询。</summary>
    public IBilibiliDanmuHistoryService History { get; } = history;
    /// <summary>收到消息留档。</summary>
    public IReceivedDanmuArchiveService? Archive { get; } = archive;
    /// <summary>被认证接口拒绝时更新对应账号的运行时状态。</summary>
    public IBilibiliAccountSessionService? AccountSessions { get; } = accountSessions;
}
