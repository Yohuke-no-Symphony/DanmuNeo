namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>一次牌墙查询；只有明确完整的结果才能证明没有持有某块牌子。</summary>
public sealed record AccountMedalCollection(IReadOnlyList<AccountMedal> Items, bool IsComplete);
