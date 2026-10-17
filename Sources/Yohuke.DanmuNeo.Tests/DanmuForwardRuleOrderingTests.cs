using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 转发规则批量排序、持久化与运行会话的回归测试。
/// </summary>
public class DanmuForwardRuleOrderingTests
{
    /// <summary>
    /// 按原列表的插入边界移动规则，保留原相对顺序、实例和状态同步。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("D", 0, "DABCE")]
    [InlineData("B", 5, "ACDEB")]
    [InlineData("E", 2, "ABECD")]
    [InlineData("DB", 0, "BDACE")]
    [InlineData("DB", 5, "ACEBD")]
    [InlineData("BD", 3, "ACBDE")]
    [InlineData("AC", 4, "BDACE")]
    [InlineData("BDB", 5, "ACEBD")]
    public async Task MoveRulesPreservesOriginalOrderAndSavesOnce(string selectedIds, int insertionIndex,
        string expectedOrder)
    {
        var saveCount = 0;
        using var room = CreateRoom(() =>
        {
            saveCount++;
            return Task.CompletedTask;
        });
        var originalRules = room.ForwardRules.ToDictionary(rule => rule.Id);
        var stateList = room.State.ForwardRules;
        var ruleList = room.ForwardRules;
        var changes = new List<NotifyCollectionChangedAction>();
        void ObserveMove(object? sender, NotifyCollectionChangedEventArgs e)
        {
            changes.Add(e.Action);
            Assert.Equal(stateList, ruleList.Select(rule => rule.State));
        }

        room.ForwardRules.CollectionChanged += ObserveMove;
        var selected = selectedIds.Select(id => originalRules[id.ToString()]).ToArray();

        try
        {
            Assert.True(await room.MoveForwardRulesAsync(selected, insertionIndex));
        }
        finally
        {
            room.ForwardRules.CollectionChanged -= ObserveMove;
        }

        Assert.Equal(expectedOrder, string.Concat(room.ForwardRules.Select(rule => rule.Id)));
        Assert.Equal(expectedOrder, string.Concat(room.State.ForwardRules.Select(rule => rule.Id)));
        Assert.Same(stateList, room.State.ForwardRules);
        Assert.NotEmpty(changes);
        Assert.All(changes, action => Assert.Equal(NotifyCollectionChangedAction.Move, action));
        Assert.Equal(1, saveCount);

        foreach (var rule in room.ForwardRules)
        {
            Assert.Same(originalRules[rule.Id], rule);
            Assert.Same(originalRules[rule.Id].State, rule.State);
        }
    }

    /// <summary>
    /// 无效边界、空选择和顺序未变化的操作不发出集合通知或保存。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("", 0)]
    [InlineData("B", -1)]
    [InlineData("B", 6)]
    [InlineData("B", 1)]
    [InlineData("B", 2)]
    [InlineData("BC", 1)]
    [InlineData("BC", 2)]
    [InlineData("BC", 3)]
    [InlineData("ABCDE", 0)]
    [InlineData("ABCDE", 2)]
    [InlineData("ABCDE", 5)]
    public async Task UnchangedOrInvalidMoveDoesNotNotifyOrSave(string selectedIds, int insertionIndex)
    {
        var saveCount = 0;
        using var room = CreateRoom(() =>
        {
            saveCount++;
            return Task.CompletedTask;
        });
        var originalRules = room.ForwardRules.ToArray();
        var changes = new List<NotifyCollectionChangedAction>();
        room.ForwardRules.CollectionChanged += (_, e) => changes.Add(e.Action);
        var selected = selectedIds.Select(id => originalRules.Single(rule => rule.Id == id.ToString())).ToArray();

        Assert.False(await room.MoveForwardRulesAsync(selected, insertionIndex));

        Assert.Equal(originalRules, room.ForwardRules);
        Assert.Equal(originalRules.Select(rule => rule.State), room.State.ForwardRules);
        Assert.Empty(changes);
        Assert.Equal(0, saveCount);
    }

    /// <summary>
    /// 选集中含任何外部实例时整批拒绝，即使它与当前规则具有相同 ID。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MoveRejectsEntireSelectionContainingForeignRule(bool reuseId)
    {
        var saveCount = 0;
        using var room = CreateRoom(() =>
        {
            saveCount++;
            return Task.CompletedTask;
        });
        var originalRules = room.ForwardRules.ToArray();
        using var foreignRule = new DanmuForwardRuleViewModel(new()
        {
            Id = reuseId ? originalRules[1].Id : "foreign"
        }, _ =>
        {
        });

        Assert.False(await room.MoveForwardRulesAsync([originalRules[1], foreignRule], 5));

        Assert.Equal(originalRules, room.ForwardRules);
        Assert.Equal(originalRules.Select(rule => rule.State), room.State.ForwardRules);
        Assert.Equal(0, saveCount);
    }

    /// <summary>
    /// 房间释放后，迟到的拖放不再修改或保存规则。
    /// </summary>
    [AvaloniaFact]
    public async Task DisposedRoomRejectsMove()
    {
        var saveCount = 0;
        var room = CreateRoom(() =>
        {
            saveCount++;
            return Task.CompletedTask;
        });
        var originalRules = room.ForwardRules.ToArray();
        room.Dispose();

        Assert.False(await room.MoveForwardRulesAsync([originalRules[1]], 5));

        Assert.Equal(originalRules, room.ForwardRules);
        Assert.Equal(originalRules.Select(rule => rule.State), room.State.ForwardRules);
        Assert.Equal(0, saveCount);
    }

    /// <summary>
    /// 保存异常向调用方传播，同时保持状态列表与界面顺序一致。
    /// </summary>
    [AvaloniaFact]
    public async Task MovePropagatesSaveFailure()
    {
        using var room = CreateRoom(() => Task.FromException(new IOException("排序保存失败")));

        await Assert.ThrowsAsync<IOException>(() => room.MoveForwardRulesAsync([room.ForwardRules[1]], 5));

        Assert.Equal("ACDEB", string.Concat(room.ForwardRules.Select(rule => rule.Id)));
        Assert.Equal(room.State.ForwardRules, room.ForwardRules.Select(rule => rule.State));
    }

    /// <summary>
    /// 多选排序写入配置文件，重新加载房间后仍保持顺序。
    /// </summary>
    [AvaloniaFact]
    public async Task ReorderedRulesKeepOrderAfterStateReload()
    {
        using var context = new DanmuForwardViewTestContext();

        foreach (var id in new[] { "B", "C", "D" })
        {
            await context.Room.AddForwardRuleStateAsync(new()
            {
                Id = id
            });
        }

        var originalRules = context.Room.ForwardRules.ToArray();
        var expectedOrder = new[] { "B", "C", originalRules[0].Id, "D" };

        Assert.True(await context.Room.MoveForwardRulesAsync([originalRules[3], originalRules[0]], 4));

        var savedState = await context.StateService.LoadAsync();
        var savedRoom = savedState.Workspaces[0].LiveRooms
            .Single(room => room.Id == context.Room.Id);
        using var reloadedRoom = new LiveRoomTabViewModel(savedRoom);
        Assert.Equal(expectedOrder, reloadedRoom.ForwardRules.Select(rule => rule.Id));
        Assert.Same(originalRules[0], context.Room.ForwardRules[2]);
    }

    /// <summary>
    /// 排序保留监听与用户名查询结果，随后仍按指定账号和符号转发一次。
    /// </summary>
    [AvaloniaFact]
    public async Task ReorderingKeepsRunningSessionAndUserName()
    {
        using var context = new DanmuForwardViewTestContext();
        await context.Room.AddForwardRuleStateAsync(new()
        {
            Id = "second", SenderUid = "84"
        });
        await context.Room.AddForwardRuleStateAsync(new()
        {
            Id = "third"
        });
        await Task.WhenAll(context.Room.ForwardRules.Select(rule => rule.UserNameLookupTask));
        var rule = context.Room.ForwardRules[0];
        var ruleState = rule.State;
        var userName = rule.SenderUserName;
        var requestedUids = context.UserNameService.RequestedUids.ToArray();

        try
        {
            rule.IsEnabled = true;
            await context.SocketFactory.Socket.StartedTask.WaitAsync(TimeSpan.FromSeconds(2));
            var status = rule.StatusText;
            Assert.True(await context.Room.MoveForwardRulesAsync([rule], context.Room.ForwardRules.Count));

            Assert.Same(rule, context.Room.ForwardRules[2]);
            Assert.Same(ruleState, context.Room.State.ForwardRules[2]);
            Assert.Equal(userName, rule.SenderUserName);
            Assert.Equal(requestedUids, context.UserNameService.RequestedUids);
            Assert.Equal(status, rule.StatusText);
            Assert.Single(context.SocketFactory.Requests);
            Assert.False(context.SocketFactory.Socket.DisposedTask.IsCompleted);
            context.SocketFactory.Socket.EmitDanmu(new()
            {
                RoomId = "100", Uid = 42, UserName = "发送者", Content = "排序后继续转发"
            });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("[排序后继续转发]", Assert.Single(context.SendService.SentMessages));
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

    private static LiveRoomTabViewModel CreateRoom(Func<Task> saveState)
    {
        var room = new LiveRoomTabViewModel(new LiveRoomTabState
        {
            ForwardRules =
            [
                .. "ABCDE".Select(id => new DanmuForwardRuleState
                {
                    Id = id.ToString()
                })
            ]
        });
        room.Configure(_ => null, (_, _) => null, () => new(), () => new(), () => [], _ => [],
            new FakeDanmuSendService(), new FakeRoomAvatarService(), saveState);
        return room;
    }
}
