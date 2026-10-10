namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 一页主播搜索结果。
/// </summary>
public class LiveRoomSearchPage
{
    /// <summary>
    /// 当前页结果。
    /// </summary>
    public IReadOnlyList<LiveRoomSearchResult> Results { get; init; } = [];

    /// <summary>
    /// 当前页码。
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// 是否还有后续页。
    /// </summary>
    public bool HasMore { get; init; }
}
