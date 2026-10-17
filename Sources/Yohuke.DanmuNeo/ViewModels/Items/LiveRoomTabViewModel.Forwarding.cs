using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class LiveRoomTabViewModel
{
    /// <summary>
    /// 按输入顺序批量追加转发规则副本，统一生成新 ID、保持禁用并保存一次。
    /// </summary>
    /// <param name="ruleStates">待复制的规则配置；不会修改输入状态或复用其状态实例。</param>
    /// <returns>实际新增的规则；空批次或已释放房间返回空列表。</returns>
    /// <remarks>在 UI 线程调用；不启动监听，空符号采用当前默认组，保存失败时传播异常。</remarks>
    public async Task<IReadOnlyList<DanmuForwardRuleViewModel>> AddForwardRuleStatesAsync(
        IReadOnlyCollection<DanmuForwardRuleState> ruleStates)
    {
        ArgumentNullException.ThrowIfNull(ruleStates);

        if (isDisposed || ruleStates.Count == 0)
        {
            return [];
        }

        var copiedStates = ruleStates.Select(state => new DanmuForwardRuleState
        {
            IsEnabled = false,
            SourceWorkspaceId = state.SourceWorkspaceId,
            SourceRoomStateId = state.SourceRoomStateId,
            SenderUid = state.SenderUid,
            ContentPattern = state.ContentPattern,
            AccountOverrideId = state.AccountOverrideId,
            MarkSymbolGroupId = string.IsNullOrWhiteSpace(state.MarkSymbolGroupId)
                ? getMarkSymbolGroup?.Invoke().Id ?? ""
                : state.MarkSymbolGroupId
        }).ToArray();
        var addedRules = copiedStates.Select(CreateForwardRuleViewModel).ToArray();

        foreach (var rule in addedRules)
        {
            State.ForwardRules.Add(rule.State);
            ForwardRules.Add(rule);
        }

        RefreshForwardSourceRooms();
        await SaveStateAsync();
        return addedRules;
    }

    /// <summary>
    /// 按原列表顺序，将选中的转发规则整体移到指定插入边界并保存。
    /// </summary>
    /// <param name="rules">当前房间拥有的规则实例；重复实例只移动一次。</param>
    /// <param name="insertionIndex">移动前列表的插入边界，范围为零至规则数量，包含末尾。</param>
    /// <returns>顺序发生变化且保存完成时返回 true；无效选集、边界或未变化时返回 false。</returns>
    /// <remarks>在 UI 线程调用；保留规则、状态与监听实例，保存失败时传播异常。</remarks>
    public async Task<bool> MoveForwardRulesAsync(IReadOnlyCollection<DanmuForwardRuleViewModel> rules,
        int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (isDisposed || rules.Count == 0 || insertionIndex < 0 || insertionIndex > ForwardRules.Count)
        {
            return false;
        }

        var originalRules = ForwardRules.ToList();
        var selectedRules = new HashSet<DanmuForwardRuleViewModel>(rules, ReferenceEqualityComparer.Instance);

        if (!selectedRules.IsSubsetOf(originalRules))
        {
            return false;
        }

        var nextRules = originalRules.Where(rule => !selectedRules.Contains(rule)).ToList();
        var adjustedIndex = insertionIndex - originalRules.Take(insertionIndex).Count(selectedRules.Contains);
        nextRules.InsertRange(adjustedIndex, originalRules.Where(selectedRules.Contains));

        if (originalRules.SequenceEqual(nextRules, ReferenceEqualityComparer.Instance))
        {
            return false;
        }

        for (var index = 0; index < nextRules.Count; index++)
        {
            var rule = nextRules[index];

            if (ReferenceEquals(ForwardRules[index], rule))
            {
                continue;
            }

            var oldIndex = ForwardRules.IndexOf(rule);
            State.ForwardRules.RemoveAt(oldIndex);
            State.ForwardRules.Insert(index, rule.State);
            ForwardRules.Move(oldIndex, index);
        }

        await SaveStateAsync();
        return true;
    }
}
