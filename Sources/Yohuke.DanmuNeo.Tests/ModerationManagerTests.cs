using System.Threading;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证显式账号、跨工作区扫描、权限复核与旧请求隔离。</summary>
public class ModerationManagerTests
{
    /// <summary>构造不查询；只有全局默认账号可以成为初始选择。</summary>
    [AvaloniaFact]
    public void ConstructorDoesNotQueryAndDoesNotInventAnAccountDefault()
    {
        using var fixture = new ModerationManagerTestContext();
        using var noDefault = new ModerationManagerTestContext(false);

        Assert.Same(fixture.Accounts[0], fixture.Manager.SelectedAccount);
        Assert.Null(noDefault.Manager.SelectedAccount);
        Assert.Equal(0, fixture.Service.PermissionRequestCount);
    }

    /// <summary>使用显式选择的账号扫描所有房间，忽略覆盖并合并短长号。</summary>
    [AvaloniaFact]
    public async Task ScanUsesOneAccountAcrossWorkspacesAndDeduplicatesCanonicalRoom()
    {
        using var fixture = new ModerationManagerTestContext();
        fixture.Workspaces.Add(new(new() { LiveRooms = [new() { RoomId = "00100" }, new() { RoomId = "99" }] }, _ => "账号"));
        fixture.Service.PermissionResponseAsync = (roomId, _, _) => Task.FromResult(new RoomModerationPermission
        {
            RoomId = roomId == "99" ? 100 : long.Parse(roomId), AccountUid = 10, OwnerUid = 20, IsRoomAdmin = true
        });
        fixture.Workspaces[0].SelectedRoom = null;

        await fixture.Manager.ActivateAsync();

        Assert.Equal(2, fixture.Manager.ManageableRooms.Count);
        Assert.Equal(2, fixture.Service.ListRequests.Count);
        Assert.All(fixture.Service.ListRequests, request => Assert.Equal(fixture.Accounts[0].Cookie, request.Cookie));
        Assert.Equal(new long[] { 100, 200 }, fixture.Manager.Records.Select(record => record.RoomId));
    }

    /// <summary>一个房间查询失败不抹掉另一个房间的成功结果。</summary>
    [AvaloniaFact]
    public async Task PartialRoomFailureRetainsOtherRoomResults()
    {
        using var fixture = new ModerationManagerTestContext();
        fixture.Service.ListResponseAsync = (roomId, _, _) => roomId == 200
            ? Task.FromException<IReadOnlyList<ModerationRecord>>(new InvalidOperationException("离线错误"))
            : Task.FromResult<IReadOnlyList<ModerationRecord>>([new() { RoomId = roomId, RecordId = 1, UserUid = 30 }]);

        await fixture.Manager.ActivateAsync();

        Assert.Equal(100, Assert.Single(fixture.Manager.Records).RoomId);
        Assert.Single(fixture.Manager.Issues);
    }

    /// <summary>旧账号即使忽略取消迟到返回，也不能污染新账号列表。</summary>
    [AvaloniaFact]
    public async Task AccountSwitchRejectsLateScanResults()
    {
        using var fixture = new ModerationManagerTestContext();
        var pending = new TaskCompletionSource<IReadOnlyList<ModerationRecord>>();
        fixture.Service.ListResponseAsync = (_, _, _) => pending.Task;
        var oldScan = fixture.Manager.ActivateAsync();
        var oldToken = fixture.Service.ListRequests.Single().Token;
        fixture.Service.ListResponseAsync = (_, _, _) => Task.FromResult<IReadOnlyList<ModerationRecord>>([]);
        fixture.Manager.SelectedAccount = fixture.Accounts[1];
        await fixture.Manager.RefreshTask;
        pending.SetResult([new() { RecordId = 1, RoomId = 100, UserUid = 30 }]);
        await oldScan;

        Assert.True(oldToken.IsCancellationRequested);
        Assert.Empty(fixture.Manager.Records);
        Assert.All(fixture.Service.ListRequests.Skip(1), request => Assert.Equal(fixture.Accounts[1].Cookie, request.Cookie));
    }

    /// <summary>当前账号被删除后清空选择，不偷偷退回另一账号。</summary>
    [AvaloniaFact]
    public async Task RemovingSelectedAccountClearsWithoutFallback()
    {
        using var fixture = new ModerationManagerTestContext();
        await fixture.Manager.ActivateAsync();

        fixture.Accounts.RemoveAt(0);

        Assert.Null(fixture.Manager.SelectedAccount);
        Assert.Empty(fixture.Manager.Records);
        Assert.False(fixture.Manager.CanManage);
    }

    /// <summary>隐藏取消读取但保留内容；再次显示只刷新发生变更的房间。</summary>
    [AvaloniaFact]
    public async Task HiddenPanelDefersDirtyRoomRefreshAndRetainsSelection()
    {
        using var fixture = new ModerationManagerTestContext();
        await fixture.Manager.ActivateAsync();
        var selected = fixture.Manager.SelectedAccount;
        var target = fixture.Manager.SelectedTargetRoom;
        fixture.Manager.Deactivate();
        fixture.Service.RaiseChanged(100);

        Assert.Equal(2, fixture.Service.ListRequests.Count);
        Assert.Equal(2, fixture.Manager.Records.Count);
        await fixture.Manager.ActivateAsync();

        Assert.Equal(3, fixture.Service.ListRequests.Count);
        Assert.Equal(100, fixture.Service.ListRequests[^1].RoomId);
        Assert.Same(selected, fixture.Manager.SelectedAccount);
        Assert.Equal(target?.RoomId, fixture.Manager.SelectedTargetRoom?.RoomId);
    }

    /// <summary>右键入口的服务事件在可见面板中只刷新受影响房间。</summary>
    [AvaloniaFact]
    public async Task SharedServiceChangeRefreshesVisibleRoom()
    {
        using var fixture = new ModerationManagerTestContext();
        await fixture.Manager.ActivateAsync();

        fixture.Service.RaiseChanged(200);
        await fixture.Manager.RefreshTask;

        Assert.Equal(3, fixture.Service.ListRequests.Count);
        Assert.Equal(200, fixture.Service.ListRequests[^1].RoomId);
    }

    /// <summary>解除请求前权限被撤销时不发送写请求。</summary>
    [AvaloniaFact]
    public async Task UnmuteRechecksPermissionAndUsesRecordId()
    {
        using var fixture = new ModerationManagerTestContext();
        await fixture.Manager.ActivateAsync();
        var record = fixture.Manager.Records[0];
        fixture.Service.PermissionResponseAsync = (_, _, _) => Task.FromResult(new RoomModerationPermission
        {
            RoomId = 100, AccountUid = 10, OwnerUid = 20, IsRoomAdmin = false
        });

        await fixture.Manager.UnmuteUserAsync(record);
        Assert.Empty(fixture.Service.UnmuteRequests);
        fixture.Service.PermissionResponseAsync = (_, _, _) => Task.FromResult(new RoomModerationPermission
        {
            RoomId = 100, AccountUid = 10, OwnerUid = 20, IsRoomAdmin = true
        });
        await fixture.Manager.UnmuteUserAsync(record);

        var request = Assert.Single(fixture.Service.UnmuteRequests);
        Assert.Equal(record.RecordId, request.RecordId);
        Assert.Equal(record.RoomId, request.RoomId);
    }

    /// <summary>切换 Cookie 会取消正在复核的写操作；旧权限响应不得写入。</summary>
    [AvaloniaFact]
    public async Task CookieChangeCancelsMutationBeforeDispatch()
    {
        using var fixture = new ModerationManagerTestContext();
        await fixture.Manager.ActivateAsync();
        var permission = new TaskCompletionSource<RoomModerationPermission>();
        fixture.Service.PermissionResponseAsync = (_, _, _) => permission.Task;
        var operation = fixture.Manager.UnmuteUserAsync(fixture.Manager.Records[0]);
        var oldToken = fixture.Service.PermissionTokens[^1];
        fixture.Manager.Deactivate();
        fixture.Accounts[0].Cookie = "SESSDATA=replaced;bili_jct=replaced";
        permission.SetResult(new() { RoomId = 100, AccountUid = 10, OwnerUid = 20, IsRoomAdmin = true });
        await operation;

        Assert.True(oldToken.IsCancellationRequested);
        Assert.Empty(fixture.Service.UnmuteRequests);
        Assert.Empty(fixture.Manager.Records);
    }

    /// <summary>主动搜索不依赖弹幕；新查询隔离忽略取消的旧结果。</summary>
    [AvaloniaFact]
    public async Task NewUserQueryIsolatesLateResultsAndCanMuteResolvedUid()
    {
        using var fixture = new ModerationManagerTestContext();
        await fixture.Manager.ActivateAsync();
        var oldPage = new TaskCompletionSource<ModerationUserSearchPage>();
        fixture.Service.SearchResponseAsync = (_, _, _) => oldPage.Task;
        fixture.Manager.SearchKeyword = "旧词";
        var oldSearch = fixture.Manager.SearchUsersAsync();
        fixture.Manager.SearchKeyword = "新词";
        fixture.Service.SearchResponseAsync = (_, _, _) => Task.FromResult(new ModerationUserSearchPage
        {
            Users = [new() { Uid = 31, UserName = "新用户" }]
        });
        await fixture.Manager.SearchUsersAsync();
        oldPage.SetResult(new() { Users = [new() { Uid = 30 }] });
        await oldSearch;
        var user = Assert.Single(fixture.Manager.UserSearchResults);
        await fixture.Manager.MuteUserAsync(user);

        Assert.Equal(31, user.Uid);
        Assert.Equal(31, Assert.Single(fixture.Service.MuteRequests).UserUid);
        Assert.True(fixture.Service.SearchRequests[0].Token.IsCancellationRequested);
    }

    /// <summary>明确未登录统一上报账号会话，关闭后续写入口。</summary>
    [AvaloniaFact]
    public async Task AuthenticationFailureExpiresSelectedAccount()
    {
        using var fixture = new ModerationManagerTestContext();
        fixture.Service.PermissionResponseAsync = (_, _, _) => Task.FromException<RoomModerationPermission>(
            new BilibiliAuthenticationException("未登录"));

        await fixture.Manager.ActivateAsync();

        Assert.Equal(AccountValidationStatus.Expired, fixture.Sessions.GetState(fixture.Accounts[0].Id).Status);
        Assert.False(fixture.Manager.CanManage);
    }
    /// <summary>忽略取消的旧读取尚未结束时，重新显示也会重试被中断的房间。</summary>
    [AvaloniaFact]
    public async Task ReactivationRetriesInterruptedRoomBeforeOldRequestCompletes()
    {
        using var fixture = new ModerationManagerTestContext();
        await fixture.Manager.ActivateAsync();
        var oldPage = new TaskCompletionSource<IReadOnlyList<ModerationRecord>>();
        fixture.Service.ListResponseAsync = (_, _, _) => oldPage.Task;
        fixture.Service.RaiseChanged(100);
        var oldRead = fixture.Manager.RefreshTask;
        var oldToken = fixture.Service.ListRequests[^1].Token;
        fixture.Manager.Deactivate();
        fixture.Service.ListResponseAsync = (roomId, _, _) => Task.FromResult<IReadOnlyList<ModerationRecord>>
            ([new() { RoomId = roomId, RecordId = 99, UserUid = 31 }]);

        await fixture.Manager.ActivateAsync();

        Assert.True(oldToken.IsCancellationRequested);
        Assert.Equal(4, fixture.Service.ListRequests.Count);
        Assert.Equal(99, fixture.Manager.Records.Single(record => record.RoomId == 100).RecordId);
        oldPage.SetResult([new() { RoomId = 100, RecordId = 1, UserUid = 30 }]);
        await oldRead;
        Assert.Equal(99, fixture.Manager.Records.Single(record => record.RoomId == 100).RecordId);
    }

    /// <summary>未变化的房间在刷新后保留选项对象，避免 ComboBox 将选择暂时写空。</summary>
    [AvaloniaFact]
    public async Task RefreshPreservesUnchangedRoomOptionReferences()
    {
        using var fixture = new ModerationManagerTestContext();
        await fixture.Manager.ActivateAsync();
        var selection = fixture.Manager.SelectedTargetRoom;
        var changes = 0;
        fixture.Manager.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(fixture.Manager.SelectedTargetRoom))
            {
                changes++;
            }
        };

        await fixture.Manager.RefreshAsync();

        Assert.Same(selection, fixture.Manager.SelectedTargetRoom);
        Assert.Equal(0, changes);
    }

    /// <summary>主动搜索不能禁言本人和主播；一般用户可通过分页查找。</summary>
    [AvaloniaFact]
    public async Task UserPagingDeduplicatesAndRejectsProtectedIdentities()
    {
        using var fixture = new ModerationManagerTestContext();
        await fixture.Manager.ActivateAsync();
        fixture.Manager.SearchKeyword = "用户";
        fixture.Service.SearchResponseAsync = (_, page, _) => Task.FromResult(new ModerationUserSearchPage
        {
            Page = page,
            HasMore = page == 1,
            Users = page == 1 ? [new() { Uid = 10 }, new() { Uid = 20 }] : [new() { Uid = 20 }, new() { Uid = 30 }]
        });
        await fixture.Manager.SearchUsersAsync();
        await fixture.Manager.LoadMoreUsersAsync();

        await fixture.Manager.MuteUserAsync(fixture.Manager.UserSearchResults[0]);
        await fixture.Manager.MuteUserAsync(fixture.Manager.UserSearchResults[1]);
        Assert.Empty(fixture.Service.MuteRequests);
        Assert.Equal(new[] { 1, 2 }, fixture.Service.SearchRequests.Select(request => request.Page));
        Assert.Equal(new long[] { 10, 20, 30 }, fixture.Manager.UserSearchResults.Select(user => user.Uid));
        Assert.False(fixture.Manager.HasMoreUsers);
    }

    /// <summary>删除工作区在权限复核期间撤销写操作，并移除已删除房间的名单。</summary>
    [AvaloniaFact]
    public async Task WorkspaceDeletionCancelsMutationAndRemovesItsRooms()
    {
        using var fixture = new ModerationManagerTestContext();
        await fixture.Manager.ActivateAsync();
        var pending = new TaskCompletionSource<RoomModerationPermission>();
        fixture.Service.PermissionResponseAsync = (_, _, _) => pending.Task;
        var operation = fixture.Manager.UnmuteUserAsync(fixture.Manager.Records[0]);
        var token = fixture.Service.PermissionTokens[^1];
        var deletedWorkspace = fixture.Workspaces.Single();
        fixture.Workspaces.Clear();
        deletedWorkspace.Dispose();
        pending.SetResult(new() { RoomId = 100, AccountUid = 10, OwnerUid = 20, IsRoomAdmin = true });
        await operation;
        await fixture.Manager.RefreshTask;

        Assert.True(token.IsCancellationRequested);
        Assert.Empty(fixture.Service.UnmuteRequests);
        Assert.Empty(fixture.Manager.Records);
        Assert.Empty(fixture.Manager.ManageableRooms);
    }
}
