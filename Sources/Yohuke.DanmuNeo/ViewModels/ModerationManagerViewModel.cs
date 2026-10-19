using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.ViewModels;

/// <summary>独立于当前房间的单账号禁言管理，统一观察跨工作区的房间及凭据生命周期。</summary>
public sealed partial class ModerationManagerViewModel : ViewModelBase, IDisposable
{
    private readonly ObservableCollection<WorkspaceViewModel> workspaces;
    private readonly IBilibiliModerationService moderationService;
    private readonly IBilibiliAccountSessionService accountSessions;
    private readonly IAppLogService log;
    private readonly Func<TimeSpan> getTimeout;
    private readonly HashSet<WorkspaceViewModel> observedWorkspaces = [];
    private readonly HashSet<LiveRoomTabViewModel> observedRooms = [];
    private readonly HashSet<BilibiliAccount> observedAccounts = [];
    private readonly Dictionary<string, long> roomAliases = [];
    private readonly Dictionary<long, LocalizedMessage> roomIssues = [];
    private readonly HashSet<long> dirtyRooms = [];
    private CancellationTokenSource? scanCancellation;
    private bool activeScanIsFull;
    private long activeScanContextVersion;
    private HashSet<long> activeScanRooms = [];
    private CancellationTokenSource? searchCancellation;
    private CancellationTokenSource? writeCancellation;
    private LocalizedMessage status = new("Moderation.SelectAccount");
    private long contextVersion;
    private long refreshLoopVersion;
    private bool fullRefreshRequired = true;
    private bool isActive;
    private bool isDisposed;
    private string loadedSearchKeyword = "";
    private int loadedSearchPage;

    /// <summary>创建应用级管理器；构造阶段只观察配置，不发起请求。</summary>
    public ModerationManagerViewModel(ObservableCollection<BilibiliAccount> accounts,
        ObservableCollection<WorkspaceViewModel> workspaces, IBilibiliModerationService moderationService,
        IBilibiliAccountSessionService accountSessions, IAppLogService log, Func<TimeSpan> getTimeout)
    {
        Accounts = accounts;
        this.workspaces = workspaces;
        this.moderationService = moderationService;
        this.accountSessions = accountSessions;
        this.log = log;
        this.getTimeout = getTimeout;
        selectedAccount = accounts.FirstOrDefault(account => account.IsGlobalDefault);
        Accounts.CollectionChanged += OnAccountsChanged;
        workspaces.CollectionChanged += OnWorkspacesChanged;
        moderationService.Changed += OnModerationChanged;
        accountSessions.Changed += OnAccountSessionChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        ObserveAccounts();
        ObserveRooms();
        SetStatus(selectedAccount is null ? "Moderation.SelectAccount" : "Moderation.Ready");
    }

    /// <summary>可供显式选择的账号，不使用工作区或房间覆盖账号。</summary>
    public ObservableCollection<BilibiliAccount> Accounts { get; }

    /// <summary>已确认管理权限的规范化房间。</summary>
    public ObservableCollection<ModerationRoomOption> ManageableRooms { get; } = [];

    /// <summary>合并后的各房间完整禁言名单。</summary>
    public ObservableCollection<ModerationRecord> Records { get; } = [];

    /// <summary>房间级读取错误；其他房间仍可保留结果。</summary>
    public ObservableCollection<string> Issues { get; } = [];

    /// <summary>通过 UID 或昵称主动查找的用户。</summary>
    public ObservableCollection<ModerationUser> UserSearchResults { get; } = [];

    /// <summary>面板当前状态，随语言变化重新渲染。</summary>
    public string StatusText => status.Render();

    /// <summary>当前账号的凭据是否允许管理写操作。</summary>
    public bool CanManage => !isDisposed && SelectedAccount is { } account && Accounts.Contains(account) &&
                             accountSessions.CanSend(account.Id, account.Cookie);

    /// <summary>当前扫描结果的统计。</summary>
    public string SummaryText => Localization.Format("Moderation.Summary", ManageableRooms.Count, Records.Count);

    internal Task RefreshTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    private BilibiliAccount? selectedAccount;

    [ObservableProperty]
    private ModerationRoomOption? selectedTargetRoom;

    [ObservableProperty]
    private string searchKeyword = "";

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private bool isWriting;

    [ObservableProperty]
    private bool hasMoreUsers;

    partial void OnSelectedAccountChanged(BilibiliAccount? value)
    {
        ResetAccountContext();
    }

    partial void OnSelectedTargetRoomChanged(ModerationRoomOption? value)
    {
        NotifyCommands();
    }

    partial void OnSearchKeywordChanged(string value)
    {
        CancelSearch();
        UserSearchResults.Clear();
        loadedSearchKeyword = "";
        loadedSearchPage = 0;
        HasMoreUsers = false;
        NotifyCommands();
    }

    partial void OnIsRefreshingChanged(bool value)
    {
        NotifyCommands();
    }

    partial void OnIsSearchingChanged(bool value)
    {
        NotifyCommands();
    }

    partial void OnIsWritingChanged(bool value)
    {
        NotifyCommands();
    }

    partial void OnHasMoreUsersChanged(bool value)
    {
        NotifyCommands();
    }

    /// <summary>显示面板，首次加载全部房间，此后只刷新已标记变更的房间。</summary>
    public Task ActivateAsync()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        isActive = true;
        NotifyCommands();
        return IsRefreshing ? RefreshTask : RefreshTask = RefreshPendingAsync();
    }

    /// <summary>隐藏或临时卸载时取消读取，保留已有数据与选择；已开始的写操作继续由凭据边界管理。</summary>
    public void Deactivate()
    {
        if (isDisposed)
        {
            return;
        }

        isActive = false;
        CancelScan();
        CancelSearch();
        NotifyCommands();
    }

    private ModerationAccountSnapshot? CaptureAccount(bool requireCredentials = true)
    {
        if (isDisposed || SelectedAccount is not { } account || !Accounts.Contains(account) ||
            (requireCredentials && !CanManage))
        {
            return null;
        }

        return new(account, account.Id, account.Cookie, contextVersion);
    }

    private bool IsCurrent(ModerationAccountSnapshot account)
    {
        return !isDisposed && account.Version == contextVersion &&
               ReferenceEquals(SelectedAccount, account.Account) && Accounts.Contains(account.Account) &&
               account.Account.Id == account.Id && account.Account.Cookie == account.Cookie;
    }

    private CancellationTokenSource CreateRequest(ModerationAccountSnapshot account)
    {
        return CancellationTokenSource.CreateLinkedTokenSource(
            accountSessions.GetCredentialCancellationToken(account.Id, account.Cookie));
    }

    private void ResetAccountContext()
    {
        contextVersion++;
        CancelScan();
        CancelSearch();
        CancelWrite();
        Records.Clear();
        ManageableRooms.Clear();
        SelectedTargetRoom = null;
        UserSearchResults.Clear();
        HasMoreUsers = false;
        loadedSearchKeyword = "";
        loadedSearchPage = 0;
        roomAliases.Clear();
        roomIssues.Clear();
        Issues.Clear();
        dirtyRooms.Clear();
        fullRefreshRequired = true;
        SetStatus(SelectedAccount is null ? "Moderation.SelectAccount" :
            CanManage ? "Moderation.Ready" : "Moderation.CredentialsRequired");
        NotifyCollections();
        ScheduleRefresh();
    }

    private void ObserveAccounts()
    {
        foreach (var account in observedAccounts.Where(account => !Accounts.Contains(account)).ToArray())
        {
            account.PropertyChanged -= OnAccountPropertyChanged;
            observedAccounts.Remove(account);
        }

        foreach (var account in Accounts.Where(account => !observedAccounts.Contains(account)))
        {
            observedAccounts.Add(account);
            account.PropertyChanged += OnAccountPropertyChanged;
        }
    }

    private void OnAccountsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObserveAccounts();

        if (SelectedAccount is not null && !Accounts.Contains(SelectedAccount))
        {
            SelectedAccount = null;
        }

        NotifyCommands();
    }

    private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, SelectedAccount) &&
            e.PropertyName is nameof(BilibiliAccount.Cookie) or nameof(BilibiliAccount.Id))
        {
            ResetAccountContext();
        }
    }

    private void OnAccountSessionChanged(object? sender, string accountId)
    {
        OnUiThread(() =>
        {
            if (isDisposed || SelectedAccount?.Id != accountId)
            {
                return;
            }

            OnPropertyChanged(nameof(CanManage));
            NotifyCommands();

            if (!CanManage)
            {
                CancelScan();
                CancelSearch();
                CancelWrite();
                SetStatus("Moderation.CredentialsRequired");
            }
            else
            {
                ScheduleRefresh();
            }
        });
    }

    private void ObserveRooms()
    {
        foreach (var workspace in observedWorkspaces.Where(workspace => !workspaces.Contains(workspace)).ToArray())
        {
            workspace.Rooms.CollectionChanged -= OnRoomsChanged;
            observedWorkspaces.Remove(workspace);
        }

        foreach (var workspace in workspaces.Where(workspace => !observedWorkspaces.Contains(workspace)))
        {
            observedWorkspaces.Add(workspace);
            workspace.Rooms.CollectionChanged += OnRoomsChanged;
        }

        var rooms = workspaces.SelectMany(workspace => workspace.Rooms).ToHashSet();

        foreach (var room in observedRooms.Where(room => !rooms.Contains(room)).ToArray())
        {
            room.PropertyChanged -= OnRoomPropertyChanged;
            observedRooms.Remove(room);
        }

        foreach (var room in rooms.Where(room => !observedRooms.Contains(room)))
        {
            observedRooms.Add(room);
            room.PropertyChanged += OnRoomPropertyChanged;
        }
    }

    private void OnWorkspacesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObserveRooms();
        InvalidateRooms();
    }

    private void OnRoomsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObserveRooms();
        InvalidateRooms();
    }

    private void OnRoomPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LiveRoomTabViewModel.RoomId) or nameof(LiveRoomTabViewModel.RoomName))
        {
            InvalidateRooms();
        }
    }

    private void InvalidateRooms()
    {
        contextVersion++;
        CancelScan();
        CancelSearch();
        CancelWrite();
        fullRefreshRequired = true;
        ScheduleRefresh();
    }

    private void OnModerationChanged(object? sender, long roomId)
    {
        OnUiThread(() =>
        {
            if (isDisposed || roomId <= 0)
            {
                return;
            }

            dirtyRooms.Add(roomId);
            ScheduleRefresh();
        });
    }

    private void ScheduleRefresh()
    {
        if (isActive && !isDisposed && CanManage && !IsRefreshing && !IsWriting &&
            (fullRefreshRequired || dirtyRooms.Count > 0))
        {
            RefreshTask = RefreshPendingAsync();
        }
    }

    private void NotifyCollections()
    {
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CanManage));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        SearchUsersCommand.NotifyCanExecuteChanged();
        LoadMoreUsersCommand.NotifyCanExecuteChanged();
        MuteUserCommand.NotifyCanExecuteChanged();
        UnmuteUserCommand.NotifyCanExecuteChanged();
    }

    private void SetStatus(string key, params object[] arguments)
    {
        status = new(key, arguments);
        OnPropertyChanged(nameof(StatusText));
    }

    private void RenderIssues()
    {
        Issues.Clear();

        foreach (var issue in roomIssues.Values)
        {
            Issues.Add(issue.Render());
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SummaryText));
        RenderIssues();
    }

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private void CancelScan()
    {
        refreshLoopVersion++;

        if (scanCancellation is not null && activeScanContextVersion == contextVersion)
        {
            fullRefreshRequired |= activeScanIsFull;
            dirtyRooms.UnionWith(activeScanRooms);
        }

        scanCancellation?.Cancel();
        scanCancellation?.Dispose();
        scanCancellation = null;
        activeScanRooms = [];
        IsRefreshing = false;
    }

    private void CancelSearch()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = null;
        IsSearching = false;
    }

    private void CancelWrite()
    {
        writeCancellation?.Cancel();
        writeCancellation?.Dispose();
        writeCancellation = null;
        IsWriting = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        isActive = false;
        CancelScan();
        CancelSearch();
        CancelWrite();
        Accounts.CollectionChanged -= OnAccountsChanged;
        workspaces.CollectionChanged -= OnWorkspacesChanged;
        moderationService.Changed -= OnModerationChanged;
        accountSessions.Changed -= OnAccountSessionChanged;
        Localization.LanguageChanged -= OnLanguageChanged;

        foreach (var account in observedAccounts)
        {
            account.PropertyChanged -= OnAccountPropertyChanged;
        }

        foreach (var workspace in observedWorkspaces)
        {
            workspace.Rooms.CollectionChanged -= OnRoomsChanged;
        }

        foreach (var room in observedRooms)
        {
            room.PropertyChanged -= OnRoomPropertyChanged;
        }

        observedAccounts.Clear();
        observedWorkspaces.Clear();
        observedRooms.Clear();
        NotifyCommands();
    }
}
