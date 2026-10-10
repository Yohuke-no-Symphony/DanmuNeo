using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 按昵称搜索可添加的 B 站直播间。
/// </summary>
public interface IBilibiliLiveRoomSearchService
{
    /// <summary>
    /// 获取指定页的主播搜索结果。
    /// </summary>
    Task<LiveRoomSearchPage> SearchAsync(
        string keyword,
        int page = 1,
        string? cookie = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
