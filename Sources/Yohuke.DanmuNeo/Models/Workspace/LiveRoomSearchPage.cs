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
    /// 各查找来源的问题；不会丢弃其他来源的成功结果。
    /// </summary>
    public IReadOnlyList<LiveRoomSearchIssue> Issues { get; init; } = [];

    /// <summary>
    /// 当前页码。
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// 是否还有后续页。
    /// </summary>
    public bool HasMore { get; init; }
}
