namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>一次牌墙查询；只有明确完整的结果才能证明没有持有某块牌子。</summary>
public sealed record AccountMedalCollection(IReadOnlyList<AccountMedal> Items, bool IsComplete)
{
    /// <summary>写操作已被接受，但牌墙尚未返回一致的佩戴状态。</summary>
    public bool IsWearingStatePending => WearingSyncState == MedalWearingSyncState.Pending;

    /// <summary>写后确认是否完成、等待同步或超时。</summary>
    public MedalWearingSyncState WearingSyncState { get; init; }
}
