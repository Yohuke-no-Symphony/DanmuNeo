using System.Threading;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 支持控制完成顺序的离线主播搜索服务。
/// </summary>
public class FakeLiveRoomSearchService : IBilibiliLiveRoomSearchService
{
    /// <summary>
    /// 请求记录。
    /// </summary>
    public List<(string Keyword, int Page, string? Cookie, CancellationToken Token)> Requests { get; } = [];

    /// <summary>
    /// 搜索响应工厂。
    /// </summary>
    public Func<string, int, Task<LiveRoomSearchPage>> RespondAsync { get; set; } =
        (_, _) => Task.FromResult(new LiveRoomSearchPage());

    /// <inheritdoc/>
    public Task<LiveRoomSearchPage> SearchAsync(
        string keyword,
        int page = 1,
        string? cookie = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        Requests.Add((keyword, page, cookie, cancellationToken));
        return RespondAsync(keyword, page);
    }
}
