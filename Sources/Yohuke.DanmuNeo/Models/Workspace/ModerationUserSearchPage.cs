namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>任意 B 站用户搜索结果页。</summary>
public sealed class ModerationUserSearchPage
{
    /// <summary>当前页用户。</summary>
    public IReadOnlyList<ModerationUser> Users { get; init; } = [];
    /// <summary>当前页码。</summary>
    public int Page { get; init; } = 1;
    /// <summary>是否还有下一页。</summary>
    public bool HasMore { get; init; }
}
