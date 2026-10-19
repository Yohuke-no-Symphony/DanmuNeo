using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.ViewModels;

/// <summary>新增房间的查找、关注和离线添加流程。</summary>
public partial class MainWindowViewModel
{
    private CancellationTokenSource? liveRoomSearchTokenSource;
    private string activeLiveRoomSearchKeyword = "";
    private int liveRoomSearchPage;
    private WorkspaceViewModel? observedRoomSearchWorkspace;
    private readonly List<BilibiliAccount> observedRoomSearchAccounts = [];
    private string? roomSearchAccountId;
    private string? roomSearchCookie;

    internal Task LiveRoomSearchTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty] private string liveRoomSearchText = "";

    [ObservableProperty] private string liveRoomSearchStatus = Localization.Get("App.Search.Hint");

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SearchLiveRoomsCommand), nameof(LoadMoreLiveRoomsCommand))]
    private bool isLiveRoomSearching;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadMoreLiveRoomsCommand))]
    private bool hasMoreLiveRoomSearchResults;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchLiveRoomsCommand), nameof(LoadMoreLiveRoomsCommand),
        nameof(AddSearchedRoomCommand))]
    private bool isAddingRoomToWorkspace;

    /// <summary>
    /// 当前弹窗的主播搜索结果。
    /// </summary>
    public ObservableCollection<LiveRoomSearchResult> LiveRoomSearchResults { get; } = [];

    /// <summary>发起新查找；空词读取关注，纯数字精确查 UID 和房号。</summary>
    [RelayCommand(CanExecute = nameof(CanSearchLiveRooms), AllowConcurrentExecutions = true)]
    public Task SearchLiveRoomsAsync()
    {
        return LiveRoomSearchTask = StartLiveRoomSearchAsync(false);
    }

    private Task StartLiveRoomSearchAsync(bool automatic)
    {
        if (!IsAddDialogOpen || !IsAddingRoom || IsAddingRoomToWorkspace || isDisposed)
        {
            return Task.CompletedTask;
        }

        CancelLiveRoomSearch();
        LiveRoomSearchResults.Clear();
        HasMoreLiveRoomSearchResults = false;
        liveRoomSearchPage = 0;
        activeLiveRoomSearchKeyword = LiveRoomSearchText.Trim();
        CaptureRoomSearchAccount();

        if (activeLiveRoomSearchKeyword.Length == 0 && string.IsNullOrWhiteSpace(roomSearchCookie))
        {
            SetLocalizedProperty(nameof(LiveRoomSearchStatus), "Services.BilibiliRoomSearch.FollowingLoginRequired");
            return Task.CompletedTask;
        }

        return SearchLiveRoomPageAsync(1, automatic);
    }

    /// <summary>加载当前昵称关键词的下一页。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadMoreLiveRooms), AllowConcurrentExecutions = true)]
    public Task LoadMoreLiveRoomsAsync()
    {
        return CanLoadMoreLiveRooms()
            ? LiveRoomSearchTask = SearchLiveRoomPageAsync(liveRoomSearchPage + 1)
            : Task.CompletedTask;
    }

    /// <summary>将查找结果加入弹窗指定的工作区。</summary>
    [RelayCommand(CanExecute = nameof(CanAddSearchedRoom))]
    public async Task AddSearchedRoomAsync(LiveRoomSearchResult? result)
    {
        if (CanAddSearchedRoom(result) && addDialogWorkspace is { } workspace)
        {
            await AddRoomToWorkspaceAsync(workspace, result!.RoomId, result.OwnerName, result.OwnerUid, result.ShortRoomId);
        }
    }

    private bool CanSearchLiveRooms()
    {
        return !isDisposed && IsAddDialogOpen && IsAddingRoom && !IsAddingRoomToWorkspace;
    }

    private bool CanLoadMoreLiveRooms()
    {
        return CanSearchLiveRooms() && !IsLiveRoomSearching && HasMoreLiveRoomSearchResults;
    }

    private bool CanAddSearchedRoom(LiveRoomSearchResult? result)
    {
        return !isDisposed && IsAddDialogOpen && IsAddingRoom && !IsAddingRoomToWorkspace &&
               result is not null && LiveRoomSearchResults.Contains(result) &&
               addDialogWorkspace is { } workspace && Workspaces.Contains(workspace);
    }

    private async Task SearchLiveRoomPageAsync(int page, bool automatic = false)
    {
        var workspace = addDialogWorkspace;

        if (workspace is null || !Workspaces.Contains(workspace))
        {
            CloseAddDialog();
            return;
        }

        var tokenSource = new CancellationTokenSource();
        liveRoomSearchTokenSource = tokenSource;
        IsLiveRoomSearching = true;
        SetLocalizedProperty(nameof(LiveRoomSearchStatus), activeLiveRoomSearchKeyword.Length == 0
            ? "App.Search.LoadingFollowing" : "App.Search.Searching");
        var accountId = roomSearchAccountId;
        var cookie = roomSearchCookie;

        try
        {
            var resultPage = await liveRoomSearchService.SearchAsync(
                activeLiveRoomSearchKeyword, page, cookie,
                TimeSpan.FromSeconds(Settings.TimeoutSeconds), tokenSource.Token);

            if (!IsCurrentLiveRoomSearch(tokenSource, accountId, cookie))
            {
                return;
            }

            if (accountId is not null && cookie is not null && resultPage.Issues.Any(issue =>
                    issue.Source == LiveRoomSearchSource.Following && issue.ApiCode == -101))
            {
                accountSessions.ReportAuthenticationFailure(accountId, cookie);
            }

            foreach (var result in resultPage.Results)
            {
                var existing = LiveRoomSearchResults.FirstOrDefault(item => item.RoomId == result.RoomId);

                if (existing is null)
                {
                    LiveRoomSearchResults.Add(result);
                }
                else
                {
                    existing.Sources |= result.Sources;
                }
            }

            liveRoomSearchPage = page;
            HasMoreLiveRoomSearchResults = resultPage.HasMore;
            var statusKey = LiveRoomSearchResults.Count > 0 ? "App.Search.Found"
                : resultPage.Issues.Count > 0 ? "App.Search.Incomplete"
                : activeLiveRoomSearchKeyword.Length == 0 ? "App.Search.FollowingEmpty" : "App.Search.Empty";
            var count = LiveRoomSearchResults.Count;
            SetLocalizedProperty(nameof(LiveRoomSearchStatus), () =>
            {
                var status = Localization.Format(statusKey, count);
                return resultPage.Issues.Count == 0 ? status
                    : string.Join(Environment.NewLine, new[] { status }.Concat(resultPage.Issues.Select(issue => issue.DisplayMessage)));
            });

            if (resultPage.Issues.Count > 0)
            {
                Log.Warning(Localization.Format("App.Log.RoomSearchWorkspace", workspace.Name), LiveRoomSearchStatus);
            }
            else if (!automatic)
            {
                Log.Information(Localization.Format("App.Log.RoomSearchWorkspace", workspace.Name), LiveRoomSearchStatus);
            }
        }
        catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentLiveRoomSearch(tokenSource, accountId, cookie))
            {
                SetLocalizedProperty(nameof(LiveRoomSearchStatus), "App.Search.Failed", new Func<string>(() => LocalizedExceptionText.Get(exception)));
                LogErrorOnce(Localization.Format("App.Log.RoomSearchWorkspace", workspace.Name), LiveRoomSearchStatus, exception);
            }
        }
        finally
        {
            if (ReferenceEquals(liveRoomSearchTokenSource, tokenSource))
            {
                liveRoomSearchTokenSource = null;
                IsLiveRoomSearching = false;
                AddSearchedRoomCommand.NotifyCanExecuteChanged();
            }

            tokenSource.Dispose();
        }
    }

    private bool IsCurrentLiveRoomSearch(CancellationTokenSource tokenSource, string? accountId, string? cookie)
    {
        var account = addDialogWorkspace is { } workspace
            ? accountSelectionService.Resolve(State, workspace.State, null) : null;
        return !isDisposed && IsAddDialogOpen && IsAddingRoom && !tokenSource.IsCancellationRequested &&
               ReferenceEquals(liveRoomSearchTokenSource, tokenSource) && account?.Id == accountId && account?.Cookie == cookie;
    }

    private void CancelLiveRoomSearch()
    {
        var tokenSource = liveRoomSearchTokenSource;
        liveRoomSearchTokenSource = null;
        tokenSource?.Cancel();
        IsLiveRoomSearching = false;
    }

    private void ResetLiveRoomSearch()
    {
        addDialogSession++;
        CancelLiveRoomSearch();
        EndRoomSearchObservation();
        LiveRoomSearchText = "";
        LiveRoomSearchResults.Clear();
        HasMoreLiveRoomSearchResults = false;
        activeLiveRoomSearchKeyword = "";
        liveRoomSearchPage = 0;
        SetLocalizedProperty(nameof(LiveRoomSearchStatus), "App.Search.Hint");
        SearchLiveRoomsCommand.NotifyCanExecuteChanged();
        AddSearchedRoomCommand.NotifyCanExecuteChanged();
    }

    private void BeginRoomSearchObservation()
    {
        EndRoomSearchObservation();
        observedRoomSearchWorkspace = addDialogWorkspace;

        if (observedRoomSearchWorkspace is not null)
        {
            observedRoomSearchWorkspace.PropertyChanged += OnRoomSearchWorkspaceChanged;
        }

        Accounts.CollectionChanged += OnRoomSearchAccountsChanged;
        ObserveRoomSearchAccounts();
        CaptureRoomSearchAccount();
    }

    private void EndRoomSearchObservation()
    {
        Accounts.CollectionChanged -= OnRoomSearchAccountsChanged;

        if (observedRoomSearchWorkspace is not null)
        {
            observedRoomSearchWorkspace.PropertyChanged -= OnRoomSearchWorkspaceChanged;
            observedRoomSearchWorkspace = null;
        }

        foreach (var account in observedRoomSearchAccounts)
        {
            account.PropertyChanged -= OnRoomSearchAccountChanged;
        }

        observedRoomSearchAccounts.Clear();
    }

    private void ObserveRoomSearchAccounts()
    {
        foreach (var account in observedRoomSearchAccounts)
        {
            account.PropertyChanged -= OnRoomSearchAccountChanged;
        }

        observedRoomSearchAccounts.Clear();

        foreach (var account in Accounts)
        {
            observedRoomSearchAccounts.Add(account);
            account.PropertyChanged += OnRoomSearchAccountChanged;
        }
    }

    private void CaptureRoomSearchAccount()
    {
        var account = addDialogWorkspace is { } workspace
            ? accountSelectionService.Resolve(State, workspace.State, null) : null;
        roomSearchAccountId = account?.Id;
        roomSearchCookie = account?.Cookie;
    }

    private void OnRoomSearchAccountsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObserveRoomSearchAccounts();
        InvalidateRoomSearchForAccountChange(true);
    }

    private void OnRoomSearchWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(WorkspaceViewModel.AccountOverrideId))
        {
            InvalidateRoomSearchForAccountChange();
        }
    }

    private void OnRoomSearchAccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(BilibiliAccount.Cookie) or nameof(BilibiliAccount.Id) or nameof(BilibiliAccount.IsGlobalDefault))
        {
            InvalidateRoomSearchForAccountChange();
        }
    }

    private void InvalidateRoomSearchForAccountChange(bool force = false)
    {
        var oldAccountId = roomSearchAccountId;
        var oldCookie = roomSearchCookie;
        CaptureRoomSearchAccount();

        if (!force && oldAccountId == roomSearchAccountId && oldCookie == roomSearchCookie)
        {
            return;
        }

        CancelLiveRoomSearch();
        LiveRoomSearchResults.Clear();
        HasMoreLiveRoomSearchResults = false;
        liveRoomSearchPage = 0;
        SetLocalizedProperty(nameof(LiveRoomSearchStatus), "App.Search.AccountChanged");
        AddSearchedRoomCommand.NotifyCanExecuteChanged();
    }

    private async Task AddRoomToWorkspaceAsync(WorkspaceViewModel workspace, string roomId, string roomName,
        string ownerUid, string shortRoomId = "")
    {
        if (isDisposed || IsAddingRoomToWorkspace || !Workspaces.Contains(workspace))
        {
            return;
        }

        if (!long.TryParse(roomId, out var parsedRoomId) || parsedRoomId <= 0)
        {
            SetLocalizedProperty(nameof(LiveRoomSearchStatus), "App.Search.InvalidRoomId");
            Log.Warning(Localization.Format("App.Log.RoomWorkspace", workspace.Name), LiveRoomSearchStatus);
            return;
        }

        var session = addDialogSession;
        IsAddingRoomToWorkspace = true;

        try
        {
            roomId = parsedRoomId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            SelectedWorkspace = workspace;
            var hasShortRoomId = long.TryParse(shortRoomId, out var parsedShortRoomId) && parsedShortRoomId > 0;
            var room = workspace.Rooms.FirstOrDefault(existing =>
                long.TryParse(existing.RoomId, out var existingRoomId) &&
                (existingRoomId == parsedRoomId || hasShortRoomId && existingRoomId == parsedShortRoomId));

            if (room is null)
            {
                room = workspace.AddRoom(roomId, roomName, ownerUid);
                ConfigureRoom(room, workspace);
            }

            workspace.SelectedRoom = room;
            workspace.IsExpanded = true;
            RefreshTreeSelection();
            RefreshForwardSourceRooms();
            await SaveAsync();
            Log.Information(Localization.Format("App.Log.RoomWorkspace", workspace.Name), Localization.Format("App.Room.Selected", room.Header, room.RoomId));

            if (session == addDialogSession)
            {
                NewRoomId = "";
                NewRoomName = "";
                NewRoomOwnerUid = "";
                CloseAddDialog();
            }
        }
        catch (Exception exception)
        {
            LogErrorOnce(Localization.Format("App.Log.RoomWorkspace", workspace.Name), Localization.Get("App.Room.AddFailed"), exception);

            if (session == addDialogSession)
            {
                SetLocalizedProperty(nameof(LiveRoomSearchStatus), "App.Room.AddFailedDetail", new Func<string>(() => LocalizedExceptionText.Get(exception)));
            }
        }
        finally
        {
            IsAddingRoomToWorkspace = false;
        }
    }

}
