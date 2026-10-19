using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 按昵称、UID、房号或关注发现可添加的 B 站直播间。
/// </summary>
public interface IBilibiliLiveRoomSearchService
{
    /// <summary>
    /// 空词读取正在直播的关注，纯数字并行精确查 UID 和房号，其他关键词按页搜索昵称。
    /// </summary>
    Task<LiveRoomSearchPage> SearchAsync(
        string keyword,
        int page = 1,
        string? cookie = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
