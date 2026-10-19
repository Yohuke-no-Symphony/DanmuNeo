namespace Yohuke.DanmuNeo.Services;

/// <summary>直播间接收模块的应用级依赖。</summary>
public sealed class LiveRoomReceptionServices(IBilibiliDanmuHistoryService history,
    IReceivedDanmuArchiveService? archive = null)
{
    /// <summary>近期弹幕查询。</summary>
    public IBilibiliDanmuHistoryService History { get; } = history;
    /// <summary>收到消息留档。</summary>
    public IReceivedDanmuArchiveService? Archive { get; } = archive;
}
