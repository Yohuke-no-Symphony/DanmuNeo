using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 搜索与工作区添加流程的离线测试。
/// </summary>
public class LiveRoomSearchViewModelTests
{
    /// <summary>
    /// 搜索裁剪关键词并使用目标工作区账号；空关键词不请求。
    /// </summary>
    [Fact]
    public async Task SearchTrimsKeywordAndUsesWorkspaceAccount()
    {
        using var context = new LiveRoomSearchTestContext();
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(viewModel.Workspaces[0]);
        viewModel.LiveRoomSearchText = "  ";
        await viewModel.SearchLiveRoomsAsync();
        Assert.Empty(context.SearchService.Requests);
        viewModel.LiveRoomSearchText = "  主播  ";
        await viewModel.SearchLiveRoomsAsync();

        var request = Assert.Single(context.SearchService.Requests);
        Assert.Equal("主播", request.Keyword);
        Assert.Equal("buvid3=test-account", request.Cookie);
        Assert.Contains("没有找到", viewModel.LiveRoomSearchStatus);
        Assert.False(viewModel.IsLiveRoomSearching);
    }

    /// <summary>
    /// 后完成的旧请求不会覆盖新结果或清除新请求的忙碌状态。
    /// </summary>
    [Fact]
    public async Task StaleSearchDoesNotOverwriteNewSearch()
    {
        using var context = new LiveRoomSearchTestContext();
        var first = new TaskCompletionSource<LiveRoomSearchPage>();
        var second = new TaskCompletionSource<LiveRoomSearchPage>();
        context.SearchService.RespondAsync = (keyword, _) => keyword == "旧" ? first.Task : second.Task;
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(null);
        viewModel.LiveRoomSearchText = "旧";
        var oldSearch = viewModel.SearchLiveRoomsAsync();
        viewModel.LiveRoomSearchText = "新";
        var newSearch = viewModel.SearchLiveRoomsAsync();
        first.SetResult(new() { Results = [LiveRoomSearchTestContext.CreateResult("101")] });
        await oldSearch;
        Assert.True(viewModel.IsLiveRoomSearching);
        Assert.Empty(viewModel.LiveRoomSearchResults);
        second.SetResult(new() { Results = [LiveRoomSearchTestContext.CreateResult("102")] });
        await newSearch;

        Assert.Equal("102", Assert.Single(viewModel.LiveRoomSearchResults).RoomId);
        Assert.True(context.SearchService.Requests[0].Token.IsCancellationRequested);
        Assert.False(viewModel.IsLiveRoomSearching);
    }

    /// <summary>
    /// 关闭与重开会话后，旧错误无法污染新弹窗。
    /// </summary>
    [Fact]
    public async Task ReopenedDialogIgnoresOldError()
    {
        using var context = new LiveRoomSearchTestContext();
        var pending = new TaskCompletionSource<LiveRoomSearchPage>();
        context.SearchService.RespondAsync = (_, _) => pending.Task;
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(null);
        viewModel.LiveRoomSearchText = "主播";
        var search = viewModel.SearchLiveRoomsAsync();
        viewModel.CloseAddDialog();
        viewModel.OpenAddRoomDialog(viewModel.Workspaces[1]);
        var status = viewModel.LiveRoomSearchStatus;
        pending.SetException(new InvalidOperationException("旧请求失败"));
        await search;

        Assert.Equal(status, viewModel.LiveRoomSearchStatus);
        Assert.Empty(viewModel.LiveRoomSearchResults);
        Assert.False(viewModel.IsLiveRoomSearching);
    }

    /// <summary>
    /// 旧请求未响应取消时，重开的弹窗仍可以通过 UI 命令搜索。
    /// </summary>
    [Fact]
    public async Task ReopenedDialogCanExecuteCommandBeforeOldSearchCompletes()
    {
        using var context = new LiveRoomSearchTestContext();
        var pending = new TaskCompletionSource<LiveRoomSearchPage>();
        context.SearchService.RespondAsync = (keyword, _) => keyword == "旧"
            ? pending.Task
            : Task.FromResult(new LiveRoomSearchPage { Results = [LiveRoomSearchTestContext.CreateResult("102")] });
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(null);
        viewModel.LiveRoomSearchText = "旧";
        var oldSearch = viewModel.SearchLiveRoomsCommand.ExecuteAsync(null);
        Assert.False(viewModel.SearchLiveRoomsCommand.CanExecute(null));
        viewModel.CloseAddDialog();
        viewModel.OpenAddRoomDialog(null);
        viewModel.LiveRoomSearchText = "新";
        Assert.True(viewModel.SearchLiveRoomsCommand.CanExecute(null));
        await viewModel.SearchLiveRoomsCommand.ExecuteAsync(null);
        pending.SetResult(new() { Results = [LiveRoomSearchTestContext.CreateResult("101")] });
        await oldSearch;

        Assert.Equal("102", Assert.Single(viewModel.LiveRoomSearchResults).RoomId);
    }

    /// <summary>
    /// 加载更多沿用原关键词并合并重复房间。
    /// </summary>
    [Fact]
    public async Task LoadMoreUsesOriginalQueryAndDeduplicates()
    {
        using var context = new LiveRoomSearchTestContext();
        context.SearchService.RespondAsync = (_, page) => Task.FromResult(new LiveRoomSearchPage
        {
            Page = page,
            HasMore = page == 1,
            Results = page == 1
                ? [LiveRoomSearchTestContext.CreateResult("100")]
                : [LiveRoomSearchTestContext.CreateResult("100"), LiveRoomSearchTestContext.CreateResult("101")]
        });
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(null);
        viewModel.LiveRoomSearchText = "主播";
        await viewModel.SearchLiveRoomsAsync();
        viewModel.LiveRoomSearchText = "另一个主播";
        await viewModel.LoadMoreLiveRoomsAsync();

        Assert.Equal(2, viewModel.LiveRoomSearchResults.Count);
        Assert.Equal(new[] { 1, 2 }, context.SearchService.Requests.Select(request => request.Page));
        Assert.All(context.SearchService.Requests, request => Assert.Equal("主播", request.Keyword));
        Assert.False(viewModel.HasMoreLiveRoomSearchResults);
    }

    /// <summary>
    /// 搜索结果添加到原目标工作区，并保存房间身份、排序与选中状态。
    /// </summary>
    [Fact]
    public async Task AddSearchResultPersistsToCapturedWorkspace()
    {
        using var context = new LiveRoomSearchTestContext();
        var result = LiveRoomSearchTestContext.CreateResult();
        context.SearchService.RespondAsync = (_, _) => Task.FromResult(new LiveRoomSearchPage { Results = [result] });
        var viewModel = context.ViewModel;
        var target = viewModel.Workspaces[0];
        viewModel.OpenAddRoomDialog(target);
        viewModel.LiveRoomSearchText = "主播";
        await viewModel.SearchLiveRoomsAsync();
        viewModel.SelectedWorkspace = viewModel.Workspaces[1];
        await viewModel.AddSearchedRoomAsync(result);

        Assert.Empty(viewModel.Workspaces[1].Rooms);
        var room = Assert.Single(target.Rooms);
        Assert.Equal("42", room.OwnerUid);
        Assert.Equal("主播昵称", room.RoomName);
        Assert.Same(target, viewModel.SelectedWorkspace);
        Assert.False(viewModel.IsAddDialogOpen);
        var savedWorkspace = context.StateService.Load().Workspaces.Single(workspace => workspace.Id == target.Id);
        var savedRoom = Assert.Single(savedWorkspace.LiveRooms);
        Assert.Equal("100", savedRoom.RoomId);
        Assert.Equal("42", savedRoom.OwnerUid);
        Assert.Equal("主播昵称", savedRoom.RoomName);
        Assert.Equal(0, savedRoom.SortOrder);
        Assert.Equal(savedRoom.Id, savedWorkspace.SelectedLiveRoomId);
        Assert.Contains("100", context.AvatarService.RequestedRooms);
    }

    /// <summary>
    /// 再次添加相同房间只选中现有标签页。
    /// </summary>
    [Fact]
    public async Task DuplicateResultSelectsExistingRoom()
    {
        using var context = new LiveRoomSearchTestContext();
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(null);
        viewModel.NewRoomId = "100";
        await viewModel.ConfirmAddDialogAsync();
        var existing = viewModel.Workspaces[0].Rooms.Single();
        existing.RoomId = "00100";
        viewModel.OpenAddRoomDialog(null);
        var result = LiveRoomSearchTestContext.CreateResult();
        viewModel.LiveRoomSearchResults.Add(result);
        await viewModel.AddSearchedRoomAsync(result);

        Assert.Same(existing, Assert.Single(viewModel.Workspaces[0].Rooms));
        Assert.Same(existing, viewModel.Workspaces[0].SelectedRoom);
    }

    /// <summary>
    /// 当前搜索失败时保留明确的错误说明并退出忙碌状态。
    /// </summary>
    [Fact]
    public async Task SearchFailureIsVisible()
    {
        using var context = new LiveRoomSearchTestContext();
        context.SearchService.RespondAsync = (_, _) => throw new InvalidOperationException("请求被拦截");
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(null);
        viewModel.LiveRoomSearchText = "主播";
        await viewModel.SearchLiveRoomsAsync();

        Assert.Equal("搜索失败：请求被拦截", viewModel.LiveRoomSearchStatus);
        Assert.False(viewModel.IsLiveRoomSearching);
        Assert.Empty(viewModel.LiveRoomSearchResults);
    }

    /// <summary>
    /// 删除目标工作区后不能把结果加入已经脱离状态的对象。
    /// </summary>
    [Fact]
    public async Task RemovedWorkspaceRejectsResult()
    {
        using var context = new LiveRoomSearchTestContext();
        var viewModel = context.ViewModel;
        var target = viewModel.Workspaces[0];
        viewModel.OpenAddRoomDialog(target);
        var result = LiveRoomSearchTestContext.CreateResult();
        viewModel.LiveRoomSearchResults.Add(result);
        await viewModel.DeleteWorkspaceAsync(target);
        await viewModel.AddSearchedRoomAsync(result);

        Assert.Empty(target.Rooms);
        Assert.Empty(viewModel.Workspaces.Single().Rooms);
        Assert.False(viewModel.IsAddDialogOpen);
    }

    /// <summary>
    /// 释放视图模型会取消搜索并忽略后到结果。
    /// </summary>
    [Fact]
    public async Task DisposeCancelsPendingSearch()
    {
        using var context = new LiveRoomSearchTestContext();
        var pending = new TaskCompletionSource<LiveRoomSearchPage>();
        context.SearchService.RespondAsync = (_, _) => pending.Task;
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(null);
        viewModel.LiveRoomSearchText = "主播";
        var search = viewModel.SearchLiveRoomsAsync();
        viewModel.Dispose();
        pending.SetResult(new() { Results = [LiveRoomSearchTestContext.CreateResult()] });
        await search;

        Assert.True(context.SearchService.Requests.Single().Token.IsCancellationRequested);
        Assert.Empty(viewModel.LiveRoomSearchResults);
    }
}
