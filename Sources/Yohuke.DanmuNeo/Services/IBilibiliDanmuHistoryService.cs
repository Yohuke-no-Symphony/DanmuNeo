using Yohuke.DanmuNeo.Apis.Models.Bilibili;

namespace Yohuke.DanmuNeo.Services;

/// <summary>查询直播间近期普通弹幕，不发布实时事件。</summary>
public interface IBilibiliDanmuHistoryService
{
    /// <summary>读取服务器当前保留的普通弹幕。</summary>
    Task<IReadOnlyList<BilibiliDanmuMessage>> GetRecentAsync(string roomId, string cookie,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
