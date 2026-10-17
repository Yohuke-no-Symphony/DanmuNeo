using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 批量粘贴转发规则的独立配置、持久化与会话生命周期测试。
/// </summary>
public class DanmuForwardRulePasteTests
{
    /// <summary>
    /// 批量规则保持输入顺序，全部生成新 ID 并禁用，仅刷新来源和保存一次。
    /// </summary>
    [AvaloniaFact]
    public async Task BatchPasteClonesRulesInOrderAndSavesOnce()
    {
        var saveCount = 0;
        var refreshCount = 0;
        var socketFactory = new FakeLiveDanmuSocketFactory();
        using var room = CreateRoom(() =>
        {
            saveCount++;
            return Task.CompletedTask;
        }, socketFactory, _ =>
        {
            refreshCount++;
            return [CreateSource()];
        });
        var first = CreateRule("first", "42");
        var second = CreateRule("second", "84");
        var ruleList = room.ForwardRules;
        var stateList = room.State.ForwardRules;
        var changes = new List<NotifyCollectionChangedAction>();

        void ObserveAddition(object? sender, NotifyCollectionChangedEventArgs e)
        {
            changes.Add(e.Action);
            Assert.Equal(stateList, ruleList.Select(rule => rule.State));
            Assert.All(ruleList, rule => Assert.False(rule.IsEnabled));
        }

        ruleList.CollectionChanged += ObserveAddition;
        IReadOnlyList<DanmuForwardRuleViewModel> added;

        try
        {
            added = await room.AddForwardRuleStatesAsync([first, second]);
        }
        finally
        {
            ruleList.CollectionChanged -= ObserveAddition;
        }

        Assert.Equal(["42", "84"], added.Select(rule => rule.SenderUid));
        Assert.Equal(added, ruleList);
        Assert.Equal(added.Select(rule => rule.State), stateList);
        Assert.Equal(2, added.Select(rule => rule.Id).Distinct().Count());
        Assert.Equal(2, changes.Count);
        Assert.All(changes, action => Assert.Equal(NotifyCollectionChangedAction.Add, action));
        Assert.Equal(1, refreshCount);
        Assert.Equal(1, saveCount);
        Assert.Empty(socketFactory.Requests);

        foreach (var (input, output) in new[] { (first, added[0]), (second, added[1]) })
        {
            Assert.NotSame(input, output.State);
            Assert.NotEqual(input.Id, output.Id);
            Assert.False(output.IsEnabled);
            Assert.Equal("未启用", output.StatusText);
            Assert.Equal(input.SourceWorkspaceId, output.State.SourceWorkspaceId);
            Assert.Equal(input.SourceRoomStateId, output.State.SourceRoomStateId);
            Assert.Equal(input.SenderUid, output.SenderUid);
            Assert.Equal(input.ContentPattern, output.ContentPattern);
            Assert.Equal(input.AccountOverrideId, output.AccountOverrideId);
            Assert.Equal(input.MarkSymbolGroupId, output.MarkSymbolGroupId);
        }

        Assert.Equal("first", first.Id);
        Assert.Equal("second", second.Id);
        Assert.True(first.IsEnabled);
        Assert.True(second.IsEnabled);
    }

    /// <summary>
    /// 重复粘贴同一状态会得到独立副本，后续编辑不影响原状态或其它副本。
    /// </summary>
    [AvaloniaFact]
    public async Task RepeatedInputStateCreatesIndependentDisabledRules()
    {
        using var room = CreateRoom(() => Task.CompletedTask);
        var input = CreateRule("original", "42");

        var added = await room.AddForwardRuleStatesAsync([input, input]);

        Assert.Equal(2, added.Count);
        Assert.NotSame(added[0].State, added[1].State);
        Assert.NotEqual(added[0].Id, added[1].Id);
        Assert.All(added, rule => Assert.NotEqual(input.Id, rule.Id));
        added[0].State.SenderUid = "84";
        input.ContentPattern = "caller edit";
        Assert.Equal("42", input.SenderUid);
        Assert.Equal("42", added[1].SenderUid);
        Assert.All(added, rule => Assert.Equal("^【.*?】$", rule.ContentPattern));
        Assert.True(input.IsEnabled);
        Assert.All(added, rule => Assert.False(rule.IsEnabled));
    }

    /// <summary>
    /// 空符号使用当前默认组，未知来源、账号与符号引用保持原值且不启动监听。
    /// </summary>
    [AvaloniaFact]
    public async Task PasteDefaultsOnlyEmptySymbolAndPreservesUnknownReferences()
    {
        var socketFactory = new FakeLiveDanmuSocketFactory();
        using var room = CreateRoom(() => Task.CompletedTask, socketFactory);
        var emptySymbol = CreateRule("empty-symbol", "42");
        emptySymbol.MarkSymbolGroupId = "";
        emptySymbol.AccountOverrideId = "";
        var unknownReferences = CreateRule("unknown-references", "84");
        unknownReferences.SourceWorkspaceId = "missing-workspace";
        unknownReferences.SourceRoomStateId = "missing-source";
        unknownReferences.AccountOverrideId = "missing-account";
        unknownReferences.MarkSymbolGroupId = "missing-symbol";

        var added = await room.AddForwardRuleStatesAsync([emptySymbol, unknownReferences]);

        Assert.Equal("default-group", added[0].MarkSymbolGroupId);
        Assert.Equal("", added[0].AccountOverrideId);
        Assert.Equal("", emptySymbol.MarkSymbolGroupId);
        Assert.Equal("missing-workspace|missing-source", added[1].SourceRoomKey);
        Assert.Equal("missing-account", added[1].AccountOverrideId);
        Assert.Equal("missing-symbol", added[1].MarkSymbolGroupId);
        Assert.All(added, rule => Assert.Equal("未启用", rule.StatusText));
        Assert.Empty(socketFactory.Requests);
    }

    /// <summary>
    /// 新规则复用注入的查询服务和下拉选项，不重复查询既有规则。
    /// </summary>
    [AvaloniaFact]
    public async Task PastedRulesUseInjectedUserNameLookupAndSharedOptions()
    {
        using var context = new DanmuForwardViewTestContext();
        var originalRule = context.Room.ForwardRules[0];
        await originalRule.UserNameLookupTask;

        var added = await context.Room.AddForwardRuleStatesAsync(
            [CreateRule("first", "84"), CreateRule("second", "126")]);
        await Task.WhenAll(added.Select(rule => rule.UserNameLookupTask));

        Assert.Equal([42L, 84L, 126L], context.UserNameService.RequestedUids.OrderBy(uid => uid));
        Assert.Equal(["用户84", "用户126"], added.Select(rule => rule.SenderUserName));
        Assert.Same(originalRule, context.Room.ForwardRules[0]);
        Assert.Equal("用户42", originalRule.SenderUserName);
        foreach (var rule in added)
        {
            Assert.Same(context.Room.ForwardSourceRooms, rule.SourceRoomOptions);
            Assert.Same(context.ViewModel.ForwardAccountOptions, rule.AccountOptions);
            Assert.Same(context.ViewModel.SymbolGroups, rule.SymbolGroups);
        }
        Assert.Empty(context.SocketFactory.Requests);
    }

    /// <summary>
    /// 批量粘贴写入配置，重载后保持既有规则、追加顺序、新 ID 与禁用状态。
    /// </summary>
    [AvaloniaFact]
    public async Task PastedRulesKeepOrderAndConfigurationAfterReload()
    {
        using var context = new DanmuForwardViewTestContext();
        var originalId = context.Room.ForwardRules[0].Id;
        var first = CreateRule("first", "84");
        var second = CreateRule("second", "126");
        first.AccountOverrideId = "forward-account";
        second.AccountOverrideId = "forward-account";
        first.MarkSymbolGroupId = "forward-group";
        second.MarkSymbolGroupId = "forward-group";

        var added = await context.Room.AddForwardRuleStatesAsync([first, second]);
        var state = await context.StateService.LoadAsync();
        var savedRoom = state.Workspaces[0].LiveRooms.Single(room => room.Id == context.Room.Id);
        using var reloaded = new LiveRoomTabViewModel(savedRoom);

        Assert.Equal([originalId, added[0].Id, added[1].Id], reloaded.ForwardRules.Select(rule => rule.Id));
        Assert.Equal(["84", "126"], reloaded.ForwardRules.Skip(1).Select(rule => rule.SenderUid));
        foreach (var rule in reloaded.ForwardRules.Skip(1))
        {
            Assert.False(rule.IsEnabled);
            Assert.Equal("workspace|source-room", rule.SourceRoomKey);
            Assert.Equal("forward-account", rule.AccountOverrideId);
            Assert.Equal("forward-group", rule.MarkSymbolGroupId);
            Assert.Equal("^【.*?】$", rule.ContentPattern);
        }
        Assert.True(first.IsEnabled);
        Assert.True(second.IsEnabled);
    }

    /// <summary>
    /// 空批次不修改集合、不刷新来源，也不保存。
    /// </summary>
    [AvaloniaFact]
    public async Task EmptyBatchDoesNotRefreshOrSave()
    {
        var saveCount = 0;
        var refreshCount = 0;
        using var room = CreateRoom(() =>
        {
            saveCount++;
            return Task.CompletedTask;
        }, getSources: _ =>
        {
            refreshCount++;
            return [CreateSource()];
        });

        Assert.Empty(await room.AddForwardRuleStatesAsync([]));

        Assert.Empty(room.ForwardRules);
        Assert.Empty(room.State.ForwardRules);
        Assert.Equal(0, refreshCount);
        Assert.Equal(0, saveCount);
    }

    /// <summary>
    /// 已释放房间拒绝迟到的粘贴，不修改输入、刷新来源或保存。
    /// </summary>
    [AvaloniaFact]
    public async Task DisposedRoomRejectsBatchWithoutSaving()
    {
        var saveCount = 0;
        var refreshCount = 0;
        var room = CreateRoom(() =>
        {
            saveCount++;
            return Task.CompletedTask;
        }, getSources: _ =>
        {
            refreshCount++;
            return [CreateSource()];
        });
        var input = CreateRule("original", "42");
        room.Dispose();

        Assert.Empty(await room.AddForwardRuleStatesAsync([input]));

        Assert.Empty(room.ForwardRules);
        Assert.Empty(room.State.ForwardRules);
        Assert.Equal(0, refreshCount);
        Assert.Equal(0, saveCount);
        Assert.Equal("original", input.Id);
        Assert.True(input.IsEnabled);
    }

    /// <summary>
    /// 粘贴正在监听的规则配置不会修改原规则、重建连接或重复发送。
    /// </summary>
    [AvaloniaFact]
    public async Task PastingActiveRuleKeepsOriginalSessionAndSendsOnce()
    {
        using var context = new DanmuForwardViewTestContext();
        var original = context.Room.ForwardRules[0];
        var originalId = original.Id;
        await original.UserNameLookupTask;

        try
        {
            original.IsEnabled = true;
            await context.SocketFactory.Socket.StartedTask.WaitAsync(TimeSpan.FromSeconds(2));
            var status = original.StatusText;

            var added = await context.Room.AddForwardRuleStatesAsync([original.State]);
            await added[0].UserNameLookupTask;

            Assert.Same(original, context.Room.ForwardRules[0]);
            Assert.Equal(originalId, original.Id);
            Assert.True(original.IsEnabled);
            Assert.False(Assert.Single(added).IsEnabled);
            Assert.Equal(status, original.StatusText);
            Assert.Single(context.SocketFactory.Requests);
            Assert.False(context.SocketFactory.Socket.DisposedTask.IsCompleted);
            context.SocketFactory.Socket.EmitDanmu(new()
            {
                RoomId = "100", Uid = 42, UserName = "发送者", Content = "粘贴后继续转发"
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("[粘贴后继续转发]", Assert.Single(context.SendService.SentMessages));
            Assert.Same(context.ViewModel.Accounts[1], Assert.Single(context.SendService.SentAccounts));
        }
        finally
        {
            context.Room.StopForwarding();

            if (context.SocketFactory.Socket.StartedTask.IsCompleted)
            {
                await context.SocketFactory.Socket.DisposedTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
    }

    private static DanmuForwardRuleState CreateRule(string id, string uid)
    {
        return new()
        {
            Id = id,
            IsEnabled = true,
            SourceWorkspaceId = "workspace",
            SourceRoomStateId = "source-room",
            SenderUid = uid,
            ContentPattern = "^【.*?】$",
            AccountOverrideId = "account",
            MarkSymbolGroupId = "default-group"
        };
    }

    private static ForwardSourceRoomOption CreateSource()
    {
        return new()
        {
            WorkspaceId = "workspace",
            RoomStateId = "source-room",
            RoomId = "100"
        };
    }

    private static LiveRoomTabViewModel CreateRoom(Func<Task> saveState,
        FakeLiveDanmuSocketFactory? socketFactory = null,
        Func<LiveRoomTabViewModel, IEnumerable<ForwardSourceRoomOption>>? getSources = null)
    {
        var room = new LiveRoomTabViewModel(new());
        var account = new BilibiliAccount
        {
            Id = "account", Cookie = "test-cookie"
        };
        var group = new MarkSymbolGroup
        {
            Id = "default-group"
        };
        room.Configure(_ => account, (_, _) => account, () => new(), () => group, () => [group],
            getSources ?? (_ => [CreateSource()]), new FakeDanmuSendService(), new FakeRoomAvatarService(), saveState,
            resolveAccountById: id => id == account.Id ? account : null,
            liveDanmuSocketFactory: socketFactory ?? new());
        return room;
    }
}
