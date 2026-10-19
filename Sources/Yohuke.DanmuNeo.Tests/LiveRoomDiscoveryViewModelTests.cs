using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证自动关注查询、账号变化和规范房间去重。</summary>
public class LiveRoomDiscoveryViewModelTests
{
    /// <summary>打开弹窗自动按目标工作区覆盖账号加载关注，构造本身不查询。</summary>
    [Fact]
    public void OpeningRoomDialogLoadsFollowingUsingTargetWorkspaceAccount()
    {
        using var context = new LiveRoomSearchTestContext();
        Assert.Empty(context.SearchService.FollowRequests);
        var account = new BilibiliAccount { Id = "workspace-account", Cookie = "SESSDATA=workspace" };
        context.ViewModel.State.Accounts.Add(account);
        context.ViewModel.Accounts.Add(account);
        context.ViewModel.Workspaces[1].AccountOverrideId = account.Id;

        context.ViewModel.OpenAddRoomDialog(context.ViewModel.Workspaces[1]);

        Assert.Equal(account.Cookie, Assert.Single(context.SearchService.FollowRequests).Cookie);
    }

    /// <summary>没有 Cookie 不自动查关注，但仍可匿名查数字和昵称。</summary>
    [Fact]
    public async Task AnonymousDialogKeepsNumericAndNicknameSearchAvailable()
    {
        using var context = new LiveRoomSearchTestContext();
        context.ViewModel.Accounts[0].Cookie = "";
        context.ViewModel.OpenAddRoomDialog(null);
        Assert.Empty(context.SearchService.FollowRequests);
        Assert.Contains("登录", context.ViewModel.LiveRoomSearchStatus);

        foreach (var keyword in new[] { "42", "主播" })
        {
            context.ViewModel.LiveRoomSearchText = keyword;
            await context.ViewModel.SearchLiveRoomsAsync();
        }

        Assert.Equal(2, context.SearchService.Requests.Count);
        Assert.All(context.SearchService.Requests, request => Assert.Equal("", request.Cookie));
    }

    /// <summary>Cookie 改变会立即取消旧关注请求并丢弃晚到结果。</summary>
    [Fact]
    public async Task CookieChangeCancelsOldFollowingAndClearsItsResults()
    {
        using var context = new LiveRoomSearchTestContext();
        var pending = new TaskCompletionSource<LiveRoomSearchPage>();
        context.SearchService.FollowingRespondAsync = () => pending.Task;
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(null);
        var automaticQuery = viewModel.LiveRoomSearchTask;
        viewModel.Accounts[0].Cookie = "SESSDATA=new-account";
        Assert.True(context.SearchService.FollowRequests.Single().Token.IsCancellationRequested);
        Assert.False(viewModel.IsLiveRoomSearching);
        pending.SetResult(new() { Results = [LiveRoomSearchTestContext.CreateResult()] });
        await automaticQuery;

        Assert.Empty(viewModel.LiveRoomSearchResults);
        context.SearchService.FollowingRespondAsync = () => Task.FromResult(new LiveRoomSearchPage());
        await viewModel.SearchLiveRoomsAsync();
        Assert.Equal("SESSDATA=new-account", context.SearchService.FollowRequests.Last().Cookie);
    }

    /// <summary>工作区账号覆盖改变时失效旧结果，不能添加其他账号的关注结果。</summary>
    [Fact]
    public async Task WorkspaceAccountChangeInvalidatesFollowingSelection()
    {
        using var context = new LiveRoomSearchTestContext();
        var viewModel = context.ViewModel;
        var result = LiveRoomSearchTestContext.CreateResult();
        context.SearchService.FollowingRespondAsync = () => Task.FromResult(new LiveRoomSearchPage { Results = [result] });
        viewModel.OpenAddRoomDialog(null);
        await viewModel.LiveRoomSearchTask;
        Assert.Single(viewModel.LiveRoomSearchResults);

        viewModel.Workspaces[0].AccountOverrideId = "other";
        var other = new BilibiliAccount { Id = "other", Cookie = "SESSDATA=other" };
        viewModel.State.Accounts.Add(other);
        viewModel.Accounts.Add(other);

        Assert.Empty(viewModel.LiveRoomSearchResults);
        Assert.False(viewModel.AddSearchedRoomCommand.CanExecute(result));
    }

    /// <summary>短号已有标签与规范房号搜索结果是同一房间，不创建第二个标签。</summary>
    [Fact]
    public async Task CanonicalResultSelectsExistingShortRoomTab()
    {
        using var context = new LiveRoomSearchTestContext();
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(null);
        viewModel.NewRoomId = "42";
        await viewModel.ConfirmAddDialogAsync();
        var existing = Assert.Single(viewModel.Workspaces[0].Rooms);
        viewModel.OpenAddRoomDialog(null);
        var result = new LiveRoomSearchResult
        {
            RoomId = "100", ShortRoomId = "42", OwnerUid = "7", OwnerName = "主播",
            Sources = LiveRoomSearchSource.RoomId
        };
        viewModel.LiveRoomSearchResults.Add(result);

        await viewModel.AddSearchedRoomAsync(result);

        Assert.Same(existing, Assert.Single(viewModel.Workspaces[0].Rooms));
        Assert.Same(existing, viewModel.Workspaces[0].SelectedRoom);
        Assert.Equal("42", existing.RoomId);
    }

    /// <summary>部分失败提示与成功结果同时显示。</summary>
    [Fact]
    public async Task PartialFailureKeepsResultsAndVisibleWarning()
    {
        using var context = new LiveRoomSearchTestContext();
        context.SearchService.RespondAsync = (_, _) => Task.FromResult(new LiveRoomSearchPage
        {
            Results = [LiveRoomSearchTestContext.CreateResult()],
            Issues = [new() { Source = LiveRoomSearchSource.UserId, Message = "请求被拦截", ApiCode = -412 }]
        });
        context.ViewModel.OpenAddRoomDialog(null);
        context.ViewModel.LiveRoomSearchText = "42";

        await context.ViewModel.SearchLiveRoomsAsync();

        Assert.Single(context.ViewModel.LiveRoomSearchResults);
        Assert.Contains("请求被拦截", context.ViewModel.LiveRoomSearchStatus);
    }
    /// <summary>关闭自动加载会话后晚到错误不能污染重开的窗口。</summary>
    [Fact]
    public async Task ClosingDialogCancelsAutomaticFollowing()
    {
        using var context = new LiveRoomSearchTestContext();
        var pending = new TaskCompletionSource<LiveRoomSearchPage>();
        context.SearchService.FollowingRespondAsync = () => pending.Task;
        context.ViewModel.OpenAddRoomDialog(null);
        var automaticQuery = context.ViewModel.LiveRoomSearchTask;
        context.ViewModel.CloseAddDialog();
        var status = context.ViewModel.LiveRoomSearchStatus;
        pending.SetException(new InvalidOperationException("旧关注请求失败"));
        await automaticQuery;

        Assert.True(context.SearchService.FollowRequests.Single().Token.IsCancellationRequested);
        Assert.Equal(status, context.ViewModel.LiveRoomSearchStatus);
        Assert.Empty(context.ViewModel.Log.Entries);
    }

    /// <summary>删除当前账号即使 State 集合随后才移除也应立即清理旧关注结果。</summary>
    [Fact]
    public async Task RemovingAccountImmediatelyInvalidatesFollowingResults()
    {
        using var context = new LiveRoomSearchTestContext();
        context.SearchService.FollowingRespondAsync = () => Task.FromResult(new LiveRoomSearchPage
        {
            Results = [LiveRoomSearchTestContext.CreateResult()]
        });
        context.ViewModel.OpenAddRoomDialog(null);
        await context.ViewModel.LiveRoomSearchTask;

        await context.ViewModel.DeleteAccountAsync(context.ViewModel.Accounts[0]);

        Assert.Empty(context.ViewModel.LiveRoomSearchResults);
        Assert.False(context.ViewModel.IsLiveRoomSearching);
    }

}
