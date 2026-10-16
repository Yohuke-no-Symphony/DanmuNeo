using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.ViewModels;

/// <summary>
/// 主窗口视图模型。
/// </summary>
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly AppStateService stateService;
    private readonly WorkspaceShareService workspaceShareService = new();
    private readonly AccountSelectionService accountSelectionService = new();
    private readonly DanmuSendService danmuSendService;
    private readonly IBilibiliDanmuConfigService danmuConfigService;
    private readonly MarkSymbolService markSymbolService = new();
    private readonly LyricLibraryService lyricLibraryService = new();
    private readonly MusicLyricImportService musicLyricImportService = new();
    private readonly AppDirectoryService appDirectoryService;
    private readonly AppBackupService appBackupService;
    private readonly IRoomAvatarService avatarCacheService;
    private readonly AvatarCacheService? ownedAvatarCacheService;
    private readonly EmoticonCacheService emoticonCacheService;
    private readonly IBilibiliLiveRoomSearchService liveRoomSearchService;
    private readonly IBilibiliModerationService moderationService;
    private readonly IBilibiliUserNameService userNameService;
    private CancellationTokenSource? liveRoomSearchTokenSource;
    private string activeLiveRoomSearchKeyword = "";
    private int liveRoomSearchPage;
    private long addDialogSession;
    private bool isDisposed;
    private Task workspaceMarkGroupSaveTask = Task.CompletedTask;
    private readonly BilibiliLiveStreamService liveStreamService = new();
    private readonly LivePlayerService livePlayerService = new();
    private WorkspaceViewModel? addDialogWorkspace;
    private WorkspaceViewModel? deleteDialogWorkspace;
    private LiveRoomTabViewModel? deleteDialogRoom;
    private string addDialogKind = "";
    private string deleteDialogKind = "";

    /// <summary>
    /// 初始化主窗口视图模型。
    /// </summary>
    public MainWindowViewModel()
        : this(new AppStateService())
    {
    }

    /// <summary>
    /// 初始化主窗口视图模型。
    /// </summary>
    public MainWindowViewModel(
        AppStateService stateService,
        IBilibiliLiveRoomSearchService? liveRoomSearchService = null,
        IRoomAvatarService? avatarCacheService = null,
        IBilibiliModerationService? moderationService = null,
        IBilibiliUserNameService? userNameService = null,
        IBilibiliDanmuConfigService? danmuConfigService = null)
    {
        this.stateService = stateService;
        appDirectoryService = new(stateService.ConfigDirectory);
        appDirectoryService.EnsureDirectories();
        appBackupService = new(appDirectoryService);
        emoticonCacheService = new(appDirectoryService);
        this.liveRoomSearchService = liveRoomSearchService ?? new BilibiliLiveRoomSearchService();
        this.moderationService = moderationService ?? new BilibiliModerationService();
        this.userNameService = userNameService ?? new BilibiliUserNameService();
        this.danmuConfigService = danmuConfigService ?? new BilibiliDanmuConfigService();
        danmuSendService = new(danmuConfigService: this.danmuConfigService);
        ownedAvatarCacheService = avatarCacheService is null ? new(appDirectoryService) : null;
        this.avatarCacheService = avatarCacheService ?? ownedAvatarCacheService!;
        State = stateService.Load();
        Settings = State.Settings;
        ShortcutBindingService.Normalize(Settings);
        Accounts = new(State.Accounts);
        ForwardAccountOptions = [];
        SymbolGroups = new(Settings.MarkGroups.OrderBy(group => group.SortOrder));
        ShieldReplacementRules = new(Settings.ShieldReplacementRules.OrderBy(rule => rule.SortOrder));
        LyricLibrary = new(State.LyricLibrary);
        FilteredLyricLibrary = new(lyricLibraryService.Search(State.LyricLibrary, ""));
        MusicLyricSearchResults = [];
        ShortcutBindingGroups = [];
        Workspaces = new(State.Workspaces.OrderBy(workspace => workspace.SortOrder).Select(CreateWorkspaceViewModel));
        selectedWorkspace = Workspaces.FirstOrDefault(workspace => workspace.Id == State.SelectedWorkspaceId) ??
                            Workspaces.FirstOrDefault();
        RefreshThemeModeOptions();
        RefreshShortcutBindingGroups();
        RefreshSettingsSectionOptions();
        RefreshForwardAccountOptions();
        RefreshCacheSize();
        RefreshTreeSelection();
        RefreshForwardSourceRooms();
        RestartForwarding();
    }

    /// <summary>
    /// 应用状态。
    /// </summary>
    public AppState State { get; }

    /// <summary>
    /// 全局设置。
    /// </summary>
    public AppSettings Settings { get; }

    /// <summary>
    /// 是否折叠整个工作区侧栏。
    /// </summary>
    public bool IsSidebarCollapsed
    {
        get => Settings.IsSidebarCollapsed;
        set
        {
            if (Settings.IsSidebarCollapsed == value)
            {
                return;
            }

            Settings.IsSidebarCollapsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SidebarToggleToolTip));
        }
    }

    /// <summary>
    /// 侧栏折叠按钮提示。
    /// </summary>
    public string SidebarToggleToolTip => IsSidebarCollapsed ? "展开工作区侧栏" : "折叠工作区侧栏";

    /// <summary>
    /// 切换并保存工作区侧栏的折叠状态。
    /// </summary>
    [RelayCommand]
    public async Task ToggleSidebarAsync()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        await SaveAsync();
    }

    /// <summary>
    /// 是否使用紧凑弹幕显示。
    /// </summary>
    public bool IsCompactDanmuDisplay
    {
        get => Settings.CompactDanmuDisplay;
        set
        {
            if (Settings.CompactDanmuDisplay == value)
            {
                return;
            }

            Settings.CompactDanmuDisplay = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 紧凑模式下是否允许普通弹幕自动换行。
    /// </summary>
    public bool IsCompactDanmuWrap
    {
        get => Settings.CompactDanmuWrap;
        set
        {
            if (Settings.CompactDanmuWrap == value)
            {
                return;
            }

            Settings.CompactDanmuWrap = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 紧凑模式下是否允许醒目留言自动换行。
    /// </summary>
    public bool IsCompactSuperChatWrap
    {
        get => Settings.CompactSuperChatWrap;
        set
        {
            if (Settings.CompactSuperChatWrap == value)
            {
                return;
            }

            Settings.CompactSuperChatWrap = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 是否屏蔽表情弹幕。
    /// </summary>
    public bool IsHideEmoticonDanmu
    {
        get => Settings.HideEmoticonDanmu;
        set
        {
            if (Settings.HideEmoticonDanmu == value)
            {
                return;
            }

            Settings.HideEmoticonDanmu = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 工作区列表。
    /// </summary>
    public ObservableCollection<WorkspaceViewModel> Workspaces { get; }

    /// <summary>
    /// 账号列表。
    /// </summary>
    public ObservableCollection<BilibiliAccount> Accounts { get; }

    /// <summary>
    /// 转发发送账号选项。
    /// </summary>
    public ObservableCollection<ForwardAccountOption> ForwardAccountOptions { get; }

    /// <summary>
    /// 符号组列表。
    /// </summary>
    public ObservableCollection<MarkSymbolGroup> SymbolGroups { get; }

    /// <summary>
    /// 屏蔽词替换规则。
    /// </summary>
    public ObservableCollection<ShieldReplacementRule> ShieldReplacementRules { get; }

    /// <summary>
    /// 歌词库。
    /// </summary>
    public ObservableCollection<LyricLibraryItem> LyricLibrary { get; }

    /// <summary>
    /// 过滤后的歌词库。
    /// </summary>
    public ObservableCollection<LyricLibraryItem> FilteredLyricLibrary { get; }

    /// <summary>
    /// 音乐 API 歌词搜索结果。
    /// </summary>
    public ObservableCollection<MusicLyricSearchResult> MusicLyricSearchResults { get; }

    /// <summary>
    /// 快捷键绑定分组。
    /// </summary>
    public ObservableCollection<ShortcutBindingGroupViewModel> ShortcutBindingGroups { get; }

    /// <summary>
    /// 主题选项。
    /// </summary>
    public IReadOnlyList<ThemeModeOption> ThemeModeOptions { get; } =
    [
        new(AppThemeMode.System, "系统"),
        new(AppThemeMode.Light, "浅色"),
        new(AppThemeMode.Dark, "深色")
    ];

    /// <summary>
    /// 设置页分区选项。
    /// </summary>
    public IReadOnlyList<SettingsSectionOption> SettingsSectionOptions { get; } =
    [
        new("global", "全局设置", "外观与发送"),
        new("account", "账号登录", "B 站与音乐账号绑定"),
        new("lyricLibrary", "歌词库", "本地与音乐 API 导入"),
        new("marks", "标签组", "同传与歌词开闭标记"),
        new("shieldReplacements", "替换库", "屏蔽词文本替换"),
        new("shortcuts", "快捷键", "直播间操作绑定"),
        new("workspace", "工作区设置", "名称、账号覆盖与分享"),
        new("misc", "杂项", "版本、目录与备份")
    ];

    /// <summary>
    /// 当前配色方案。
    /// </summary>
    public ThemeModeOption? SelectedThemeModeOption
    {
        get => ThemeModeOptions.FirstOrDefault(option => option.Value == Settings.ThemeMode);
        set
        {
            if (value is null || Settings.ThemeMode == value.Value)
            {
                return;
            }

            Settings.ThemeMode = value.Value;
            AppThemeService.Apply(value.Value);
            RefreshThemeModeOptions();
            OnPropertyChanged();
            _ = SaveAsync();
        }
    }

    /// <summary>
    /// 当前设置页分区。
    /// </summary>
    public SettingsSectionOption? SelectedSettingsSection
    {
        get => SettingsSectionOptions.FirstOrDefault(option => option.Key == selectedSettingsSectionKey);
        set
        {
            if (value is null || selectedSettingsSectionKey == value.Key)
            {
                return;
            }

            selectedSettingsSectionKey = value.Key;
            RefreshSettingsSectionOptions();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGlobalSettingsSelected));
            OnPropertyChanged(nameof(IsAccountSettingsSelected));
            OnPropertyChanged(nameof(IsLyricLibrarySettingsSelected));
            OnPropertyChanged(nameof(IsMarkSettingsSelected));
            OnPropertyChanged(nameof(IsShieldReplacementSettingsSelected));
            OnPropertyChanged(nameof(IsShortcutSettingsSelected));
            OnPropertyChanged(nameof(IsWorkspaceSettingsSelected));
            OnPropertyChanged(nameof(IsMiscSettingsSelected));
        }
    }

    /// <summary>
    /// 当前工作区符号组。
    /// </summary>
    public MarkSymbolGroup? SelectedWorkspaceMarkGroup
    {
        get
        {
            if (SelectedWorkspace is null)
            {
                return null;
            }

            return SymbolGroups.FirstOrDefault(group => group.Id == SelectedWorkspace.SelectedMarkGroupId) ??
                   SymbolGroups.FirstOrDefault();
        }
        set
        {
            if (!TrySelectWorkspaceMarkGroup(value))
            {
                return;
            }

            _ = QueueWorkspaceMarkGroupSave();
        }
    }

    /// <summary>
    /// 按预设显示顺序轮切当前工作区的同传样式，并保存选择。
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task CycleWorkspaceMarkGroupAsync()
    {
        if (isDisposed || SelectedWorkspace is null || SymbolGroups.Count <= 1)
        {
            return Task.CompletedTask;
        }

        var currentIndex = SymbolGroups.IndexOf(SelectedWorkspaceMarkGroup!);
        var nextGroup = SymbolGroups[(currentIndex + 1) % SymbolGroups.Count];
        return TrySelectWorkspaceMarkGroup(nextGroup) ? QueueWorkspaceMarkGroupSave() : Task.CompletedTask;
    }

    private bool TrySelectWorkspaceMarkGroup(MarkSymbolGroup? group)
    {
        if (isDisposed || SelectedWorkspace is null || group is null || !SymbolGroups.Contains(group) ||
            SelectedWorkspace.SelectedMarkGroupId == group.Id)
        {
            return false;
        }

        SelectedWorkspace.SelectedMarkGroupId = group.Id;
        OnPropertyChanged(nameof(SelectedWorkspaceMarkGroup));
        return true;
    }

    private Task QueueWorkspaceMarkGroupSave()
    {
        workspaceMarkGroupSaveTask = SaveWorkspaceMarkGroupAfterAsync(workspaceMarkGroupSaveTask);
        return workspaceMarkGroupSaveTask;
    }

    private async Task SaveWorkspaceMarkGroupAfterAsync(Task previousSave)
    {
        try
        {
            await previousSave;
            await SaveAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"保存同传样式预设失败：{exception.Message}";
        }
    }

    [ObservableProperty] private WorkspaceViewModel? selectedWorkspace;

    private string selectedSettingsSectionKey = "global";

    [ObservableProperty] private string lyricLibrarySearchText = "";

    [ObservableProperty] private string musicLyricSearchText = "";

    [ObservableProperty] private string selectedMusicLyricSource = "wy";

    [ObservableProperty] private bool isMusicLyricSearching;

    [ObservableProperty] private bool isSettingsOpen;

    [ObservableProperty] private bool isAddDialogOpen;

    [ObservableProperty] private bool isConfirmDeleteDialogOpen;

    [ObservableProperty] private bool isLyricLibraryPickerOpen;

    [ObservableProperty] private string addDialogTitle = "";

    [ObservableProperty] private string addDialogDescription = "";

    [ObservableProperty] private string addDialogPrimaryText = "";

    [ObservableProperty] private string deleteDialogTitle = "";

    [ObservableProperty] private string deleteDialogDescription = "";

    [ObservableProperty] private string deleteDialogPrimaryText = "删除";

    [ObservableProperty] private string newWorkspaceName = "";

    [ObservableProperty] private string newRoomId = "";

    [ObservableProperty] private string newRoomName = "";

    [ObservableProperty] private string newRoomOwnerUid = "";

    [ObservableProperty] private string liveRoomSearchText = "";

    [ObservableProperty] private string liveRoomSearchStatus = "输入主播昵称搜索，也可以手动添加";

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

    /// <summary>
    /// 添加弹窗实际指定的工作区名称。
    /// </summary>
    public string AddDialogWorkspaceName => addDialogWorkspace?.Name ?? "";

    [ObservableProperty] private string statusMessage = "";

    [ObservableProperty] private string cacheSizeText = "0 B";

    [ObservableProperty] private string lastBackupPath = "";

    /// <summary>
    /// 窗口标题。
    /// </summary>
    public string WindowTitle => SelectedWorkspace is null
        ? "Yohuke.DanmuNeo"
        : $"Yohuke.DanmuNeo · {SelectedWorkspace.Name}";

    /// <summary>
    /// 是否正在显示全局设置。
    /// </summary>
    public bool IsGlobalSettingsSelected => selectedSettingsSectionKey == "global";

    /// <summary>
    /// 是否正在显示账号登录设置。
    /// </summary>
    public bool IsAccountSettingsSelected => selectedSettingsSectionKey == "account";

    /// <summary>
    /// 是否正在显示歌词库设置。
    /// </summary>
    public bool IsLyricLibrarySettingsSelected => selectedSettingsSectionKey == "lyricLibrary";

    /// <summary>
    /// 是否正在显示标签组设置。
    /// </summary>
    public bool IsMarkSettingsSelected => selectedSettingsSectionKey == "marks";

    /// <summary>
    /// 是否正在显示屏蔽词替换库设置。
    /// </summary>
    public bool IsShieldReplacementSettingsSelected => selectedSettingsSectionKey == "shieldReplacements";

    /// <summary>
    /// 是否正在显示快捷键设置。
    /// </summary>
    public bool IsShortcutSettingsSelected => selectedSettingsSectionKey == "shortcuts";

    /// <summary>
    /// 是否正在显示工作区设置。
    /// </summary>
    public bool IsWorkspaceSettingsSelected => selectedSettingsSectionKey == "workspace";

    /// <summary>
    /// 是否正在显示杂项设置。
    /// </summary>
    public bool IsMiscSettingsSelected => selectedSettingsSectionKey == "misc";

    /// <summary>
    /// 当前版本。
    /// </summary>
    public string AppVersion => typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "开发版";

    /// <summary>
    /// 配置目录。
    /// </summary>
    public string ConfigDirectory => appDirectoryService.ConfigDirectory;

    /// <summary>
    /// 缓存目录。
    /// </summary>
    public string CacheDirectory => appDirectoryService.CacheDirectory;

    /// <summary>
    /// 是否存在最近导出的备份路径。
    /// </summary>
    public bool HasLastBackupPath => !string.IsNullOrWhiteSpace(LastBackupPath);

    /// <summary>
    /// QQ 音乐绑定状态。
    /// </summary>
    public string QQMusicCookieStatus => Settings.QQMusicCookieStatus;

    /// <summary>
    /// QQ 音乐 Cookie 摘要。
    /// </summary>
    public string QQMusicCookieSummary => Settings.QQMusicCookieSummary;

    /// <summary>
    /// 新增对话框是否正在创建工作区。
    /// </summary>
    public bool IsAddingWorkspace => addDialogKind == "workspace";

    /// <summary>
    /// 新增对话框是否正在创建直播间。
    /// </summary>
    public bool IsAddingRoom => addDialogKind == "room";

    /// <summary>
    /// 是否选择网易云歌词来源。
    /// </summary>
    public bool IsNetEaseMusicLyricSource => SelectedMusicLyricSource == "wy";

    /// <summary>
    /// 是否选择 QQ 音乐歌词来源。
    /// </summary>
    public bool IsQQMusicLyricSource => SelectedMusicLyricSource == "qq";

    partial void OnSelectedWorkspaceChanged(WorkspaceViewModel? value)
    {
        State.SelectedWorkspaceId = value?.Id;
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(SelectedWorkspaceMarkGroup));
        RefreshTreeSelection();
    }

    partial void OnIsAddDialogOpenChanged(bool value)
    {
        if (!value)
        {
            CancelLiveRoomSearch();
        }

        SearchLiveRoomsCommand.NotifyCanExecuteChanged();
        LoadMoreLiveRoomsCommand.NotifyCanExecuteChanged();
        AddSearchedRoomCommand.NotifyCanExecuteChanged();
    }

    partial void OnLyricLibrarySearchTextChanged(string value)
    {
        RefreshFilteredLyricLibrary();
    }

    partial void OnSelectedMusicLyricSourceChanged(string value)
    {
        OnPropertyChanged(nameof(IsNetEaseMusicLyricSource));
        OnPropertyChanged(nameof(IsQQMusicLyricSource));
    }

    partial void OnLastBackupPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasLastBackupPath));
    }

    /// <summary>
    /// 新建工作区。
    /// </summary>
    public async Task AddWorkspaceAsync()
    {
        var name = string.IsNullOrWhiteSpace(NewWorkspaceName)
            ? $"工作区 {Workspaces.Count + 1}"
            : NewWorkspaceName.Trim();
        var state = new WorkspaceState
        {
            Name = name
        };
        State.Workspaces.Add(state);
        var workspace = CreateWorkspaceViewModel(state);
        Workspaces.Add(workspace);
        SelectedWorkspace = workspace;
        NewWorkspaceName = "";
        RefreshWorkspaceSortOrder();
        await SaveAsync();
        RefreshTreeSelection();
    }

    /// <summary>
    /// 选择工作区。
    /// </summary>
    public void SelectWorkspace(WorkspaceViewModel? workspace)
    {
        if (workspace is null)
        {
            return;
        }

        if (SelectedWorkspace != workspace)
        {
            SelectedWorkspace?.SelectedRoom?.StopLivePlayer();
        }

        SelectedWorkspace = workspace;
        workspace.RefreshRoomSelection();
        RefreshTreeSelection();
    }

    /// <summary>
    /// 选择直播间。
    /// </summary>
    public void SelectRoom(LiveRoomTabViewModel? room)
    {
        if (room is null)
        {
            return;
        }

        var workspace = Workspaces.FirstOrDefault(item => item.Rooms.Contains(room));

        if (workspace is null)
        {
            return;
        }

        if (SelectedWorkspace?.SelectedRoom != room)
        {
            SelectedWorkspace?.SelectedRoom?.StopLivePlayer();
        }

        SelectedWorkspace = workspace;
        workspace.SelectedRoom = room;
        workspace.IsExpanded = true;
        RefreshTreeSelection();
    }

    /// <summary>
    /// 切换工作区展开状态。
    /// </summary>
    public void ToggleWorkspaceExpanded(WorkspaceViewModel? workspace)
    {
        if (workspace is null)
        {
            return;
        }

        workspace.IsExpanded = !workspace.IsExpanded;
    }

    /// <summary>
    /// 打开新增工作区对话框。
    /// </summary>
    public void OpenAddWorkspaceDialog()
    {
        ResetLiveRoomSearch();
        addDialogKind = "workspace";
        addDialogWorkspace = null;
        NewWorkspaceName = "";
        NewRoomName = "";
        NewRoomId = "";
        NewRoomOwnerUid = "";
        AddDialogTitle = "新建工作区";
        AddDialogDescription = "给新的同传工作空间起一个容易识别的名称。";
        AddDialogPrimaryText = "确定";
        OnPropertyChanged(nameof(IsAddingWorkspace));
        OnPropertyChanged(nameof(IsAddingRoom));
        IsAddDialogOpen = true;
    }

    /// <summary>
    /// 打开新增直播间对话框。
    /// </summary>
    public void OpenAddRoomDialog(WorkspaceViewModel? workspace)
    {
        var targetWorkspace = workspace ?? SelectedWorkspace;

        if (isDisposed || targetWorkspace is null || !Workspaces.Contains(targetWorkspace))
        {
            return;
        }

        ResetLiveRoomSearch();
        SelectedWorkspace = targetWorkspace;
        addDialogKind = "room";
        addDialogWorkspace = targetWorkspace;
        NewWorkspaceName = "";
        NewRoomName = "";
        NewRoomId = "";
        NewRoomOwnerUid = "";
        AddDialogTitle = $"添加直播间到 {targetWorkspace.Name}";
        AddDialogDescription = "按主播昵称搜索并直接添加，或手动填写直播间信息。";
        AddDialogPrimaryText = "手动添加";
        OnPropertyChanged(nameof(IsAddingWorkspace));
        OnPropertyChanged(nameof(IsAddingRoom));
        OnPropertyChanged(nameof(AddDialogWorkspaceName));
        IsAddDialogOpen = true;
    }

    /// <summary>
    /// 关闭新增对话框。
    /// </summary>
    public void CloseAddDialog()
    {
        IsAddDialogOpen = false;
        ResetLiveRoomSearch();
    }

    /// <summary>
    /// 确认新增工作区或直播间。
    /// </summary>
    public async Task ConfirmAddDialogAsync()
    {
        if (isDisposed || !IsAddDialogOpen || IsAddingRoomToWorkspace)
        {
            return;
        }

        if (addDialogKind == "workspace")
        {
            await AddWorkspaceAsync();
            IsAddDialogOpen = false;
            return;
        }

        if (!IsAddDialogOpen || addDialogKind != "room" || IsAddingRoomToWorkspace)
        {
            return;
        }

        var workspace = addDialogWorkspace ?? SelectedWorkspace;

        if (workspace is null)
        {
            return;
        }

        await AddRoomToWorkspaceAsync(workspace, NewRoomId.Trim(), NewRoomName.Trim(), NewRoomOwnerUid.Trim());
    }

    /// <summary>
    /// 按主播昵称发起新搜索。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSearchLiveRooms), AllowConcurrentExecutions = true)]
    public async Task SearchLiveRoomsAsync()
    {
        if (!IsAddDialogOpen || !IsAddingRoom || IsAddingRoomToWorkspace || isDisposed)
        {
            return;
        }

        CancelLiveRoomSearch();
        LiveRoomSearchResults.Clear();
        HasMoreLiveRoomSearchResults = false;
        liveRoomSearchPage = 0;
        activeLiveRoomSearchKeyword = LiveRoomSearchText.Trim();

        if (string.IsNullOrWhiteSpace(activeLiveRoomSearchKeyword))
        {
            LiveRoomSearchStatus = "请输入主播昵称";
            return;
        }

        await SearchLiveRoomPageAsync(1);
    }

    /// <summary>
    /// 加载当前关键词的下一页主播结果。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadMoreLiveRooms), AllowConcurrentExecutions = true)]
    public async Task LoadMoreLiveRoomsAsync()
    {
        if (CanLoadMoreLiveRooms())
        {
            await SearchLiveRoomPageAsync(liveRoomSearchPage + 1);
        }
    }

    /// <summary>
    /// 将搜索结果加入弹窗指定的工作区。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddSearchedRoom))]
    public async Task AddSearchedRoomAsync(LiveRoomSearchResult? result)
    {
        if (CanAddSearchedRoom(result) && addDialogWorkspace is { } workspace)
        {
            await AddRoomToWorkspaceAsync(workspace, result!.RoomId, result.OwnerName, result.OwnerUid);
        }
    }

    private bool CanSearchLiveRooms()
    {
        return !isDisposed && IsAddDialogOpen && IsAddingRoom && !IsLiveRoomSearching && !IsAddingRoomToWorkspace;
    }

    private bool CanLoadMoreLiveRooms()
    {
        return CanSearchLiveRooms() && HasMoreLiveRoomSearchResults;
    }

    private bool CanAddSearchedRoom(LiveRoomSearchResult? result)
    {
        return !isDisposed && IsAddDialogOpen && IsAddingRoom && !IsAddingRoomToWorkspace &&
               result is not null && LiveRoomSearchResults.Contains(result) &&
               addDialogWorkspace is { } workspace && Workspaces.Contains(workspace);
    }

    private async Task SearchLiveRoomPageAsync(int page)
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
        LiveRoomSearchStatus = "正在搜索主播…";

        try
        {
            var account = accountSelectionService.Resolve(State, workspace.State, null);
            var resultPage = await liveRoomSearchService.SearchAsync(
                activeLiveRoomSearchKeyword, page, account?.Cookie,
                TimeSpan.FromSeconds(Settings.TimeoutSeconds), tokenSource.Token);

            if (!IsCurrentLiveRoomSearch(tokenSource))
            {
                return;
            }

            foreach (var result in resultPage.Results)
            {
                if (LiveRoomSearchResults.All(existing => existing.RoomId != result.RoomId))
                {
                    LiveRoomSearchResults.Add(result);
                }
            }

            liveRoomSearchPage = page;
            HasMoreLiveRoomSearchResults = resultPage.HasMore;
            LiveRoomSearchStatus = LiveRoomSearchResults.Count == 0
                ? "没有找到匹配的主播，可以尝试其他昵称或手动添加"
                : $"已找到 {LiveRoomSearchResults.Count} 个直播间，点击添加即可加入工作区";
        }
        catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentLiveRoomSearch(tokenSource))
            {
                LiveRoomSearchStatus = $"搜索失败：{exception.Message}";
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

    private bool IsCurrentLiveRoomSearch(CancellationTokenSource tokenSource)
    {
        return !isDisposed && IsAddDialogOpen && IsAddingRoom && !tokenSource.IsCancellationRequested &&
               ReferenceEquals(liveRoomSearchTokenSource, tokenSource);
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
        LiveRoomSearchText = "";
        LiveRoomSearchResults.Clear();
        HasMoreLiveRoomSearchResults = false;
        activeLiveRoomSearchKeyword = "";
        liveRoomSearchPage = 0;
        LiveRoomSearchStatus = "输入主播昵称搜索，也可以手动添加";
        SearchLiveRoomsCommand.NotifyCanExecuteChanged();
        AddSearchedRoomCommand.NotifyCanExecuteChanged();
    }

    private async Task AddRoomToWorkspaceAsync(WorkspaceViewModel workspace, string roomId, string roomName,
        string ownerUid)
    {
        if (isDisposed || IsAddingRoomToWorkspace || !Workspaces.Contains(workspace))
        {
            return;
        }

        if (!long.TryParse(roomId, out var parsedRoomId) || parsedRoomId <= 0)
        {
            LiveRoomSearchStatus = "请输入有效的直播间 ID";
            return;
        }

        var session = addDialogSession;
        IsAddingRoomToWorkspace = true;

        try
        {
            roomId = parsedRoomId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            SelectedWorkspace = workspace;
            var room = workspace.Rooms.FirstOrDefault(existing =>
                long.TryParse(existing.RoomId, out var existingRoomId) && existingRoomId == parsedRoomId);

            if (room is null)
            {
                room = workspace.AddRoom(roomId, roomName, ownerUid);
                ConfigureRoom(room);
            }

            workspace.SelectedRoom = room;
            workspace.IsExpanded = true;
            RefreshTreeSelection();
            RefreshForwardSourceRooms();
            await SaveAsync();

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
            if (session == addDialogSession)
            {
                LiveRoomSearchStatus = $"添加直播间失败：{exception.Message}";
            }
        }
        finally
        {
            IsAddingRoomToWorkspace = false;
        }
    }

    /// <summary>
    /// 打开删除工作区确认框。
    /// </summary>
    public void OpenDeleteWorkspaceDialog(WorkspaceViewModel? workspace)
    {
        if (workspace is null || Workspaces.Count <= 1)
        {
            return;
        }

        deleteDialogKind = "workspace";
        deleteDialogWorkspace = workspace;
        deleteDialogRoom = null;
        DeleteDialogTitle = "删除工作区";
        DeleteDialogDescription = $"确定删除“{workspace.Name}”吗？该工作区下的直播间配置会一并移除。";
        DeleteDialogPrimaryText = "删除工作区";
        IsConfirmDeleteDialogOpen = true;
    }

    /// <summary>
    /// 打开删除直播间确认框。
    /// </summary>
    public void OpenDeleteRoomDialog(LiveRoomTabViewModel? room)
    {
        if (room is null)
        {
            return;
        }

        deleteDialogKind = "room";
        deleteDialogWorkspace = Workspaces.FirstOrDefault(item => item.Rooms.Contains(room));
        deleteDialogRoom = room;
        DeleteDialogTitle = "删除直播间";
        DeleteDialogDescription = $"确定删除直播间“{room.Header}”吗？运行期间的弹幕和 SC 历史会从当前工作区移除。";
        DeleteDialogPrimaryText = "删除直播间";
        IsConfirmDeleteDialogOpen = true;
    }

    /// <summary>
    /// 关闭删除确认框。
    /// </summary>
    public void CloseDeleteDialog()
    {
        IsConfirmDeleteDialogOpen = false;
        deleteDialogKind = "";
        deleteDialogWorkspace = null;
        deleteDialogRoom = null;
    }

    /// <summary>
    /// 确认删除工作区或直播间。
    /// </summary>
    public async Task ConfirmDeleteDialogAsync()
    {
        var workspace = deleteDialogWorkspace;
        var room = deleteDialogRoom;
        var kind = deleteDialogKind;
        CloseDeleteDialog();

        if (kind == "workspace")
        {
            await DeleteWorkspaceAsync(workspace);
            return;
        }

        if (kind == "room")
        {
            await RemoveRoomAsync(room);
        }
    }

    /// <summary>
    /// 删除当前工作区。
    /// </summary>
    public async Task DeleteSelectedWorkspaceAsync()
    {
        await DeleteWorkspaceAsync(SelectedWorkspace);
    }

    /// <summary>
    /// 删除指定工作区。
    /// </summary>
    public async Task DeleteWorkspaceAsync(WorkspaceViewModel? workspace)
    {
        if (workspace is null || Workspaces.Count <= 1)
        {
            return;
        }

        if (ReferenceEquals(addDialogWorkspace, workspace))
        {
            CloseAddDialog();
        }

        foreach (var room in workspace.Rooms)
        {
            room.StopListening();
            room.DisposeForwardRules();
            room.StopLivePlayer();
            room.DeactivateDanmuConfiguration();
        }

        State.Workspaces.Remove(workspace.State);
        Workspaces.Remove(workspace);
        SelectedWorkspace = Workspaces.FirstOrDefault();
        RefreshWorkspaceSortOrder();
        await SaveAsync();
        RefreshTreeSelection();
        RefreshForwardSourceRooms();
    }

    /// <summary>
    /// 添加直播间 tab。
    /// </summary>
    public async Task AddRoomAsync()
    {
        if (SelectedWorkspace is null || string.IsNullOrWhiteSpace(NewRoomId))
        {
            return;
        }

        await AddRoomToWorkspaceAsync(SelectedWorkspace, NewRoomId.Trim(), NewRoomName.Trim(), NewRoomOwnerUid.Trim());
    }

    /// <summary>
    /// 删除直播间 tab。
    /// </summary>
    public async Task RemoveRoomAsync(LiveRoomTabViewModel? room)
    {
        if (room is null)
        {
            return;
        }

        var workspace = Workspaces.FirstOrDefault(item => item.Rooms.Contains(room));

        if (workspace is null)
        {
            return;
        }

        room.StopListening();
        room.DisposeForwardRules();
        room.StopLivePlayer();
        room.DeactivateDanmuConfiguration();
        workspace.RemoveRoom(room);

        if (SelectedWorkspace == workspace)
        {
            SelectedWorkspace = workspace;
        }

        await SaveAsync();
        RefreshTreeSelection();
        RefreshForwardSourceRooms();
    }

    /// <summary>
    /// 保存直播间信息改动。
    /// </summary>
    public async Task SaveRoomInfoAsync(LiveRoomTabViewModel? room)
    {
        if (room is null)
        {
            return;
        }

        RefreshTreeSelection();
        RefreshForwardSourceRooms();
        await SaveAsync();

        if (!await room.LoadAvatarAsync(true))
        {
            StatusMessage = "主播头像获取失败，请检查直播间 ID 或主播 UID";
        }
    }

    /// <summary>
    /// 打开设置页。
    /// </summary>
    public void OpenSettings()
    {
        SelectedSettingsSection = SettingsSectionOptions.FirstOrDefault(option => option.Key == "global");
        IsSettingsOpen = true;
    }

    /// <summary>
    /// 打开同传页面内嵌歌词库选择器。
    /// </summary>
    public void OpenLyricLibraryPicker()
    {
        IsLyricLibraryPickerOpen = true;
    }

    /// <summary>
    /// 关闭同传页面内嵌歌词库选择器。
    /// </summary>
    public void CloseLyricLibraryPicker()
    {
        IsLyricLibraryPickerOpen = false;
    }

    /// <summary>
    /// 打开指定工作区设置。
    /// </summary>
    public void OpenWorkspaceSettings(WorkspaceViewModel? workspace)
    {
        if (workspace is not null)
        {
            SelectedWorkspace = workspace;
        }

        SelectedSettingsSection = SettingsSectionOptions.FirstOrDefault(option => option.Key == "workspace");
        IsSettingsOpen = true;
    }

    /// <summary>
    /// 选择设置页分区。
    /// </summary>
    public void SelectSettingsSection(SettingsSectionOption? option)
    {
        if (option is null)
        {
            return;
        }

        SelectedSettingsSection = option;
    }

    /// <summary>
    /// 开始录制快捷键。
    /// </summary>
    public void BeginRecordShortcut(ShortcutBindingViewModel? binding)
    {
        if (binding is null)
        {
            return;
        }

        CancelShortcutRecording();
        binding.IsRecording = true;
        StatusMessage = $"按下新的快捷键：{binding.Name}";
        OnPropertyChanged(nameof(IsRecordingShortcut));
    }

    /// <summary>
    /// 清除快捷键。
    /// </summary>
    public void ClearShortcut(ShortcutBindingViewModel? binding)
    {
        if (binding is null)
        {
            return;
        }

        binding.IsRecording = false;
        binding.SetGestureText("", false);
        StatusMessage = $"已清除快捷键：{binding.Name}";
        OnPropertyChanged(nameof(IsRecordingShortcut));
    }

    /// <summary>
    /// 恢复默认快捷键。
    /// </summary>
    public void ResetShortcutToDefault(ShortcutBindingViewModel? binding)
    {
        if (binding is null)
        {
            return;
        }

        binding.IsRecording = false;
        binding.SetGestureText(binding.DefaultGestureText, true);
        StatusMessage = $"已恢复默认快捷键：{binding.Name}";
        OnPropertyChanged(nameof(IsRecordingShortcut));
    }

    /// <summary>
    /// 是否正在录制快捷键。
    /// </summary>
    public bool IsRecordingShortcut => EnumerateShortcutBindings().Any(binding => binding.IsRecording);

    /// <summary>
    /// 应用录制到的快捷键。
    /// </summary>
    public bool ApplyRecordedShortcut(Key key, KeyModifiers modifiers)
    {
        var binding = EnumerateShortcutBindings().FirstOrDefault(item => item.IsRecording);

        if (binding is null)
        {
            return false;
        }

        if (key == Key.Escape)
        {
            CancelShortcutRecording();
            StatusMessage = "已取消快捷键录制";
            return true;
        }

        if (key is Key.Back or Key.Delete)
        {
            ClearShortcut(binding);
            return true;
        }

        if (!ShortcutBindingService.TryCreateGestureText(key, modifiers, out var gestureText))
        {
            StatusMessage = "请按下包含主按键的快捷键";
            return true;
        }

        binding.IsRecording = false;
        binding.SetGestureText(gestureText, true);
        StatusMessage = $"已设置快捷键：{binding.Name} {gestureText}";
        OnPropertyChanged(nameof(IsRecordingShortcut));
        return true;
    }

    /// <summary>
    /// 取消快捷键录制。
    /// </summary>
    public void CancelShortcutRecording()
    {
        foreach (var binding in EnumerateShortcutBindings())
        {
            binding.IsRecording = false;
        }

        OnPropertyChanged(nameof(IsRecordingShortcut));
    }

    /// <summary>
    /// 从弹幕创建转发规则。
    /// </summary>
    public async Task AddDanmuForwardRuleFromFeedAsync(
        LiveRoomTabViewModel sourceRoom,
        DanmuFeedItem item,
        LiveRoomTabViewModel targetRoom)
    {
        var sourceWorkspace = Workspaces.FirstOrDefault(workspace => workspace.Rooms.Contains(sourceRoom));

        if (sourceWorkspace is null)
        {
            StatusMessage = "未找到来源工作区";
            return;
        }

        if (targetRoom.Id == sourceRoom.Id)
        {
            StatusMessage = "不能将直播间转发到自身";
            return;
        }

        if (string.IsNullOrWhiteSpace(item.UserUid))
        {
            StatusMessage = "弹幕缺少用户 UID";
            return;
        }

        await targetRoom.AddForwardRuleStateAsync(new()
        {
            IsEnabled = false,
            SourceWorkspaceId = sourceWorkspace.Id,
            SourceRoomStateId = sourceRoom.Id,
            SenderUid = item.UserUid.Trim()
        });
        RefreshForwardSourceRooms();
        StatusMessage = $"已添加转发规则：{item.UserName} -> {targetRoom.Header}";
    }

    /// <summary>
    /// 选择网易云歌词来源。
    /// </summary>
    public void SelectNetEaseMusicLyricSource()
    {
        SelectedMusicLyricSource = "wy";
    }

    /// <summary>
    /// 选择 QQ 音乐歌词来源。
    /// </summary>
    public void SelectQQMusicLyricSource()
    {
        SelectedMusicLyricSource = "qq";
    }

    /// <summary>
    /// 搜索音乐 API 歌词。
    /// </summary>
    public async Task SearchMusicLyricsAsync()
    {
        if (IsMusicLyricSearching)
        {
            return;
        }

        MusicLyricSearchResults.Clear();

        if (string.IsNullOrWhiteSpace(MusicLyricSearchText))
        {
            return;
        }

        IsMusicLyricSearching = true;

        try
        {
            var results = await musicLyricImportService.SearchAsync(
                MusicLyricSearchText,
                SelectedMusicLyricSource,
                Settings.QQMusicCookie);

            foreach (var result in results)
            {
                MusicLyricSearchResults.Add(result);
            }

            StatusMessage = $"找到 {MusicLyricSearchResults.Count} 条歌词候选";
        }
        catch (Exception exception)
        {
            StatusMessage = $"音乐 API 搜索失败：{exception.Message}";
        }
        finally
        {
            IsMusicLyricSearching = false;
        }
    }

    /// <summary>
    /// 选择配色方案。
    /// </summary>
    public async Task SelectThemeModeAsync(ThemeModeOption? option)
    {
        if (option is null || Settings.ThemeMode == option.Value)
        {
            return;
        }

        Settings.ThemeMode = option.Value;
        AppThemeService.Apply(option.Value);
        RefreshThemeModeOptions();
        OnPropertyChanged(nameof(SelectedThemeModeOption));
        await SaveAsync();
    }

    /// <summary>
    /// 关闭设置页并保存。
    /// </summary>
    public async Task CloseSettingsAsync()
    {
        IsSettingsOpen = false;
        await SaveAsync();
    }

    /// <summary>
    /// 新增账号。
    /// </summary>
    public async Task AddAccountAsync()
    {
        var account = new BilibiliAccount
        {
            Name = $"账号 {Accounts.Count + 1}",
            IsGlobalDefault = Accounts.Count == 0
        };
        Accounts.Add(account);
        State.Accounts.Add(account);
        RefreshAccountLabels();
        RefreshForwardAccountOptions();
        await SaveAsync();
    }

    /// <summary>
    /// 应用 B 站浏览器登录结果。
    /// </summary>
    public async Task ApplyBilibiliBrowserLoginAsync(BrowserCookieLoginResult result, BilibiliAccount? account)
    {
        if (result.Platform != BrowserLoginPlatform.Bilibili || string.IsNullOrWhiteSpace(result.Cookie))
        {
            return;
        }

        var targetAccount = account;

        if (targetAccount is null)
        {
            targetAccount = new()
            {
                Name = $"B站账号 {Accounts.Count + 1}",
                IsGlobalDefault = Accounts.Count == 0
            };
            Accounts.Add(targetAccount);
            State.Accounts.Add(targetAccount);
            RefreshForwardAccountOptions();
        }

        targetAccount.Cookie = result.Cookie;
        RefreshAccountLabels();
        await SaveAsync();
        StatusMessage = result.Message;
    }

    /// <summary>
    /// 应用 QQ 音乐浏览器登录结果。
    /// </summary>
    public async Task ApplyQQMusicBrowserLoginAsync(BrowserCookieLoginResult result)
    {
        if (result.Platform != BrowserLoginPlatform.QQMusic || string.IsNullOrWhiteSpace(result.Cookie))
        {
            return;
        }

        Settings.QQMusicCookie = result.Cookie;
        OnPropertyChanged(nameof(QQMusicCookieStatus));
        OnPropertyChanged(nameof(QQMusicCookieSummary));
        await SaveAsync();
        StatusMessage = result.Message;
    }

    /// <summary>
    /// 删除账号。
    /// </summary>
    public async Task DeleteAccountAsync(BilibiliAccount? account)
    {
        if (account is null)
        {
            return;
        }

        Accounts.Remove(account);
        State.Accounts.Remove(account);

        foreach (var workspace in State.Workspaces)
        {
            if (workspace.AccountOverrideId == account.Id)
            {
                workspace.AccountOverrideId = null;
            }

            foreach (var room in workspace.LiveRooms)
            {
                if (room.AccountOverrideId == account.Id)
                {
                    room.AccountOverrideId = null;
                }

            }
        }

        foreach (var rule in Workspaces
                     .SelectMany(workspace => workspace.Rooms)
                     .SelectMany(room => room.ForwardRules)
                     .Where(rule => rule.AccountOverrideId == account.Id))
        {
            rule.AccountOverrideId = "";
        }

        if (Accounts.Count > 0 && Accounts.All(item => !item.IsGlobalDefault))
        {
            Accounts[0].IsGlobalDefault = true;
        }

        RefreshAccountLabels();
        RefreshForwardAccountOptions();
        await SaveAsync();
    }

    /// <summary>
    /// 设置默认账号。
    /// </summary>
    public async Task SetDefaultAccountAsync(BilibiliAccount? account)
    {
        if (account is null)
        {
            return;
        }

        foreach (var item in Accounts)
        {
            item.IsGlobalDefault = item == account;
        }

        RefreshAccountLabels();
        await SaveAsync();
    }

    /// <summary>
    /// 当前工作区使用全局账号。
    /// </summary>
    public async Task ClearWorkspaceAccountAsync()
    {
        if (SelectedWorkspace is null)
        {
            return;
        }

        SelectedWorkspace.AccountOverrideId = null;
        RefreshAccountLabels();
        await SaveAsync();
    }

    /// <summary>
    /// 当前工作区使用指定账号。
    /// </summary>
    public async Task SetWorkspaceAccountAsync(BilibiliAccount? account)
    {
        if (SelectedWorkspace is null || account is null)
        {
            return;
        }

        SelectedWorkspace.AccountOverrideId = account.Id;
        RefreshAccountLabels();
        await SaveAsync();
    }

    /// <summary>
    /// 上移工作区。
    /// </summary>
    public async Task MoveWorkspaceUpAsync(WorkspaceViewModel? workspace)
    {
        await MoveWorkspaceAsync(workspace, -1);
    }

    /// <summary>
    /// 下移工作区。
    /// </summary>
    public async Task MoveWorkspaceDownAsync(WorkspaceViewModel? workspace)
    {
        await MoveWorkspaceAsync(workspace, 1);
    }

    /// <summary>
    /// 上移直播间。
    /// </summary>
    public async Task MoveRoomUpAsync(LiveRoomTabViewModel? room)
    {
        await MoveRoomAsync(room, -1);
    }

    /// <summary>
    /// 下移直播间。
    /// </summary>
    public async Task MoveRoomDownAsync(LiveRoomTabViewModel? room)
    {
        await MoveRoomAsync(room, 1);
    }

    /// <summary>
    /// 新增符号组。
    /// </summary>
    public async Task AddSymbolGroupAsync()
    {
        var baseGroup = SelectedWorkspaceMarkGroup ??
                        SymbolGroups.FirstOrDefault() ?? MarkSymbolService.CreateDefaultGroup(Settings);
        var group = new MarkSymbolGroup
        {
            Name = $"符号组 {SymbolGroups.Count + 1}",
            TranslateOpenMark = baseGroup.TranslateOpenMark,
            TranslateCloseMark = baseGroup.TranslateCloseMark,
            LyricOpenMark = baseGroup.LyricOpenMark,
            LyricCloseMark = baseGroup.LyricCloseMark
        };
        SymbolGroups.Add(group);
        Settings.MarkGroups.Add(group);
        RefreshSymbolGroupSortOrder();

        if (SelectedWorkspace is not null)
        {
            SelectedWorkspaceMarkGroup = group;
        }

        await SaveAsync();
    }

    /// <summary>
    /// 删除符号组。
    /// </summary>
    public async Task DeleteSymbolGroupAsync(MarkSymbolGroup? group)
    {
        if (group is null || SymbolGroups.Count <= 1)
        {
            return;
        }

        SymbolGroups.Remove(group);
        Settings.MarkGroups.Remove(group);
        var fallback = SymbolGroups[0];
        RefreshSymbolGroupSortOrder();

        foreach (var workspace in Workspaces.Where(workspace => workspace.SelectedMarkGroupId == group.Id))
        {
            workspace.SelectedMarkGroupId = fallback.Id;
        }

        OnPropertyChanged(nameof(SelectedWorkspaceMarkGroup));
        await SaveAsync();
    }

    /// <summary>
    /// 切换符号组展开状态。
    /// </summary>
    public async Task ToggleSymbolGroupExpandedAsync(MarkSymbolGroup? group)
    {
        if (group is null)
        {
            return;
        }

        group.IsExpanded = !group.IsExpanded;
        await SaveAsync();
    }

    /// <summary>
    /// 上移符号组。
    /// </summary>
    public async Task MoveSymbolGroupUpAsync(MarkSymbolGroup? group)
    {
        await MoveSymbolGroupAsync(group, -1);
    }

    /// <summary>
    /// 下移符号组。
    /// </summary>
    public async Task MoveSymbolGroupDownAsync(MarkSymbolGroup? group)
    {
        await MoveSymbolGroupAsync(group, 1);
    }

    /// <summary>
    /// 新增屏蔽词替换规则。
    /// </summary>
    public async Task AddShieldReplacementRuleAsync()
    {
        var rule = new ShieldReplacementRule
        {
            SourceText = "",
            ReplacementText = ""
        };
        ShieldReplacementRules.Add(rule);
        RefreshShieldReplacementRuleSortOrder();
        await SaveAsync();
    }

    /// <summary>
    /// 删除屏蔽词替换规则。
    /// </summary>
    public async Task DeleteShieldReplacementRuleAsync(ShieldReplacementRule? rule)
    {
        if (rule is null)
        {
            return;
        }

        ShieldReplacementRules.Remove(rule);
        RefreshShieldReplacementRuleSortOrder();
        await SaveAsync();
    }

    /// <summary>
    /// 上移屏蔽词替换规则。
    /// </summary>
    public async Task MoveShieldReplacementRuleUpAsync(ShieldReplacementRule? rule)
    {
        await MoveShieldReplacementRuleAsync(rule, -1);
    }

    /// <summary>
    /// 下移屏蔽词替换规则。
    /// </summary>
    public async Task MoveShieldReplacementRuleDownAsync(ShieldReplacementRule? rule)
    {
        await MoveShieldReplacementRuleAsync(rule, 1);
    }

    /// <summary>
    /// 将当前直播间歌词保存到歌词库。
    /// </summary>
    public async Task SaveCurrentLyricToLibraryAsync()
    {
        var room = SelectedWorkspace?.SelectedRoom;

        if (room is null ||
            string.IsNullOrWhiteSpace(room.LyricInput) &&
            string.IsNullOrWhiteSpace(room.TranslatedLyricInput))
        {
            return;
        }

        var title = string.IsNullOrWhiteSpace(room.LyricTitle) ? room.Header : room.LyricTitle.Trim();
        try
        {
            UpsertLyricLibraryItem(new()
            {
                Title = title,
                Source = "local",
                Tags = title,
                HasTranslation = !string.IsNullOrWhiteSpace(room.TranslatedLyricInput),
                LyricText = room.LyricInput,
                TranslatedLyricText = room.TranslatedLyricInput
            });
            await SaveAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"歌词保存失败：{exception.Message}";
        }
    }

    /// <summary>
    /// 从本地文件导入歌词到当前直播间和歌词库。
    /// </summary>
    public async Task<bool> ImportLyricFileAsync(string filePath)
    {
        try
        {
            var item = await lyricLibraryService.CreateFromLocalFileAsync(filePath);
            var savedItem = UpsertLyricLibraryItem(item);
            UseLyricLibraryItem(savedItem);
            await SaveAsync();
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"歌词导入失败：{exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// 从本地文件导入歌词到歌词库。
    /// </summary>
    public async Task<bool> ImportLyricFileToLibraryAsync(string filePath)
    {
        try
        {
            var item = await lyricLibraryService.CreateFromLocalFileAsync(filePath);
            UpsertLyricLibraryItem(item);
            await SaveAsync();
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"歌词导入失败：{exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// 从音乐 API 导入歌词。
    /// </summary>
    public async Task ImportMusicLyricAsync(MusicLyricSearchResult? result)
    {
        if (result is null)
        {
            return;
        }

        try
        {
            var item = await musicLyricImportService.ImportAsync(result, Settings.QQMusicCookie);
            var savedItem = UpsertLyricLibraryItem(item);
            UseLyricLibraryItem(savedItem);
            await SaveAsync();

            if (IsLyricLibraryPickerOpen)
            {
                IsLyricLibraryPickerOpen = false;
            }
        }
        catch (Exception exception)
        {
            StatusMessage = $"歌词导入失败：{exception.Message}";
        }
    }

    /// <summary>
    /// 使用歌词库条目。
    /// </summary>
    public async Task UseLyricLibraryItemAsync(LyricLibraryItem? item)
    {
        if (item is null)
        {
            return;
        }

        UseLyricLibraryItem(item);
        await SaveAsync();
    }

    /// <summary>
    /// 在内嵌选择器中选择歌词库条目。
    /// </summary>
    public async Task PickLyricLibraryItemAsync(LyricLibraryItem? item)
    {
        if (item is null)
        {
            return;
        }

        UseLyricLibraryItem(item);
        IsLyricLibraryPickerOpen = false;
        await SaveAsync();
    }

    /// <summary>
    /// 删除歌词库条目。
    /// </summary>
    public async Task DeleteLyricLibraryItemAsync(LyricLibraryItem? item)
    {
        if (item is null)
        {
            return;
        }

        LyricLibrary.Remove(item);
        State.LyricLibrary.Remove(item);
        RefreshFilteredLyricLibrary();
        await SaveAsync();
    }

    /// <summary>
    /// 歌词库条目编辑后刷新并保存。
    /// </summary>
    public async Task SaveLyricLibraryEditAsync()
    {
        RefreshFilteredLyricLibrary();
        await SaveAsync();
    }

    /// <summary>
    /// 打开配置目录。
    /// </summary>
    public void OpenConfigDirectory()
    {
        OpenDirectory(ConfigDirectory, "配置目录打开失败");
    }

    /// <summary>
    /// 打开缓存目录。
    /// </summary>
    public void OpenCacheDirectory()
    {
        OpenDirectory(CacheDirectory, "缓存目录打开失败");
    }

    /// <summary>
    /// 刷新缓存体积。
    /// </summary>
    public void RefreshCacheSize()
    {
        CacheSizeText = AppBackupService.FormatSize(appBackupService.GetCacheSize());
    }

    /// <summary>
    /// 清理缓存。
    /// </summary>
    public async Task ClearCacheAsync()
    {
        appBackupService.ClearCache();

        foreach (var room in Workspaces.SelectMany(workspace => workspace.Rooms))
        {
            room.AvatarPath = "";
        }

        RefreshCacheSize();
        await SaveAsync();
        StatusMessage = "缓存已清理";
    }

    /// <summary>
    /// 导出全部配置备份。
    /// </summary>
    public async Task ExportAllBackupAsync()
    {
        try
        {
            var outputDirectory = Path.Combine(ConfigDirectory, "Exports");
            LastBackupPath = await appBackupService.ExportBackupZipAsync(outputDirectory);
            StatusMessage = $"已导出备份：{LastBackupPath}";
            OpenDirectory(outputDirectory, "备份目录打开失败");
        }
        catch (Exception exception)
        {
            StatusMessage = $"导出备份失败：{exception.Message}";
        }
    }

    /// <summary>
    /// 保存应用状态。
    /// </summary>
    public async Task SaveAsync()
    {
        Settings.SidebarWidth = AppStateService.ClampSidebarWidth(Settings.SidebarWidth);
        Settings.WindowWidth = AppStateService.ClampWindowWidth(Settings.WindowWidth);
        Settings.WindowHeight = AppStateService.ClampWindowHeight(Settings.WindowHeight);
        Settings.WorkspaceLiveColumnWidth =
            AppStateService.ClampWorkspaceColumnWidth(Settings.WorkspaceLiveColumnWidth);
        Settings.WorkspaceToolColumnWidth =
            AppStateService.ClampWorkspaceColumnWidth(Settings.WorkspaceToolColumnWidth);
        await stateService.SaveAsync(State);
        StatusMessage = $"已保存 {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>
    /// 导出当前工作区。
    /// </summary>
    public async Task ExportSelectedWorkspaceAsync(string filePath)
    {
        await ExportWorkspaceAsync(SelectedWorkspace, filePath);
    }

    /// <summary>
    /// 导出指定工作区。
    /// </summary>
    public async Task ExportWorkspaceAsync(WorkspaceViewModel? workspace, string filePath)
    {
        if (workspace is null)
        {
            return;
        }

        await workspaceShareService.ExportAsync(workspace.State, Settings, filePath);
        StatusMessage = "工作区已导出";
    }

    /// <summary>
    /// 导入工作区。
    /// </summary>
    public async Task ImportWorkspaceAsync(string filePath)
    {
        var workspaceState = await workspaceShareService.ImportAsync(State.Workspaces, filePath);
        State.Workspaces.Add(workspaceState);
        var workspace = CreateWorkspaceViewModel(workspaceState);
        Workspaces.Add(workspace);
        SelectedWorkspace = workspace;
        await SaveAsync();
        StatusMessage = "工作区已导入";
    }

    /// <summary>
    /// 获取导出文件名。
    /// </summary>
    public string GetExportFileName()
    {
        return GetExportFileName(SelectedWorkspace);
    }

    /// <summary>
    /// 获取指定工作区的导出文件名。
    /// </summary>
    public string GetExportFileName(WorkspaceViewModel? workspace)
    {
        var name = workspace?.Name ?? "workspace";
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName =
            new string(name.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
        return $"{safeName}.dnworkspace.json";
    }

    private WorkspaceViewModel CreateWorkspaceViewModel(WorkspaceState state)
    {
        var workspace = new WorkspaceViewModel(state, GetAccountName);

        foreach (var room in workspace.Rooms)
        {
            ConfigureRoom(room);
        }

        workspace.RefreshRoomSelection();
        return workspace;
    }

    private void ConfigureRoom(LiveRoomTabViewModel room)
    {
        room.Configure(
            ResolveAccount,
            ResolveAccount,
            () => Settings,
            () => ResolveMarkGroup(room),
            () => SymbolGroups,
            CreateForwardSourceRoomOptions,
            danmuSendService,
            avatarCacheService,
            SaveAsync,
            liveStreamService,
            livePlayerService,
            ResolveAccount,
            moderationService: moderationService,
            forwardAccountOptions: ForwardAccountOptions,
            userNameService: userNameService,
            danmuConfigService: danmuConfigService,
            emoticonCacheService: emoticonCacheService);
        _ = room.LoadAvatarAsync();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        ResetLiveRoomSearch();
        foreach (var room in Workspaces.SelectMany(workspace => workspace.Rooms))
        {
            room.StopListening();
            room.DisposeForwardRules();
            room.StopLivePlayer();
            room.DeactivateDanmuConfiguration();
        }

        foreach (var option in ForwardAccountOptions)
        {
            option.Dispose();
        }

        livePlayerService.Dispose();
        ownedAvatarCacheService?.Dispose();
        emoticonCacheService.Dispose();
        GC.SuppressFinalize(this);
    }

    private BilibiliAccount? ResolveAccount(LiveRoomTabViewModel room)
    {
        var workspace = Workspaces.FirstOrDefault(item => item.Rooms.Contains(room));

        if (workspace is null)
        {
            return null;
        }

        return accountSelectionService.Resolve(State, workspace.State, room.State);
    }

    private BilibiliAccount? ResolveAccount(string workspaceId, string roomStateId)
    {
        var workspace = Workspaces.FirstOrDefault(item => item.Id == workspaceId);
        var room = workspace?.Rooms.FirstOrDefault(item => item.Id == roomStateId);

        if (workspace is null)
        {
            return null;
        }

        return accountSelectionService.Resolve(State, workspace.State, room?.State);
    }

    private BilibiliAccount? ResolveAccount(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return null;
        }

        return Accounts.FirstOrDefault(account => account.Id == accountId);
    }

    private IEnumerable<ForwardSourceRoomOption> CreateForwardSourceRoomOptions(LiveRoomTabViewModel targetRoom)
    {
        foreach (var workspace in Workspaces)
        {
            foreach (var room in workspace.Rooms)
            {
                if (room.Id == targetRoom.Id)
                {
                    continue;
                }

                yield return new()
                {
                    WorkspaceId = workspace.Id,
                    WorkspaceName = workspace.Name,
                    RoomStateId = room.Id,
                    RoomId = room.RoomId,
                    RoomName = room.RoomName
                };
            }
        }
    }

    private void RefreshForwardSourceRooms()
    {
        foreach (var room in Workspaces.SelectMany(workspace => workspace.Rooms))
        {
            room.RefreshForwardSourceRooms();
        }
    }

    private void RestartForwarding()
    {
        foreach (var room in Workspaces.SelectMany(workspace => workspace.Rooms))
        {
            room.RestartForwarding();
        }
    }

    private MarkSymbolGroup ResolveMarkGroup(LiveRoomTabViewModel room)
    {
        var workspace = Workspaces.FirstOrDefault(item => item.Rooms.Contains(room)) ?? SelectedWorkspace;
        return markSymbolService.Resolve(Settings, workspace?.State);
    }

    private string GetAccountName(string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return "使用全局账号";
        }

        return Accounts.FirstOrDefault(account => account.Id == accountId)?.Name ?? "账号不存在";
    }

    private async Task MoveWorkspaceAsync(WorkspaceViewModel? workspace, int offset)
    {
        if (workspace is null)
        {
            return;
        }

        var oldIndex = Workspaces.IndexOf(workspace);
        var newIndex = oldIndex + offset;

        if (oldIndex < 0 || newIndex < 0 || newIndex >= Workspaces.Count)
        {
            return;
        }

        Workspaces.Move(oldIndex, newIndex);
        RefreshWorkspaceSortOrder();
        await SaveAsync();
    }

    private async Task MoveRoomAsync(LiveRoomTabViewModel? room, int offset)
    {
        if (room is null)
        {
            return;
        }

        var workspace = Workspaces.FirstOrDefault(item => item.Rooms.Contains(room));

        if (workspace is null || !workspace.MoveRoom(room, offset))
        {
            return;
        }

        await SaveAsync();
    }

    private async Task MoveSymbolGroupAsync(MarkSymbolGroup? group, int offset)
    {
        if (group is null)
        {
            return;
        }

        var oldIndex = SymbolGroups.IndexOf(group);
        var newIndex = oldIndex + offset;

        if (oldIndex < 0 || newIndex < 0 || newIndex >= SymbolGroups.Count)
        {
            return;
        }

        SymbolGroups.Move(oldIndex, newIndex);
        RefreshSymbolGroupSortOrder();
        await SaveAsync();
    }

    private async Task MoveShieldReplacementRuleAsync(ShieldReplacementRule? rule, int offset)
    {
        if (rule is null)
        {
            return;
        }

        var oldIndex = ShieldReplacementRules.IndexOf(rule);
        var newIndex = oldIndex + offset;

        if (oldIndex < 0 || newIndex < 0 || newIndex >= ShieldReplacementRules.Count)
        {
            return;
        }

        ShieldReplacementRules.Move(oldIndex, newIndex);
        RefreshShieldReplacementRuleSortOrder();
        await SaveAsync();
    }

    private void RefreshWorkspaceSortOrder()
    {
        State.Workspaces.Clear();

        for (var index = 0; index < Workspaces.Count; index++)
        {
            Workspaces[index].State.SortOrder = index;
            State.Workspaces.Add(Workspaces[index].State);
        }
    }

    private void RefreshSymbolGroupSortOrder()
    {
        Settings.MarkGroups.Clear();

        for (var index = 0; index < SymbolGroups.Count; index++)
        {
            SymbolGroups[index].SortOrder = index;
            Settings.MarkGroups.Add(SymbolGroups[index]);
        }
    }

    private void RefreshShieldReplacementRuleSortOrder()
    {
        Settings.ShieldReplacementRules.Clear();

        for (var index = 0; index < ShieldReplacementRules.Count; index++)
        {
            ShieldReplacementRules[index].SortOrder = index;
            Settings.ShieldReplacementRules.Add(ShieldReplacementRules[index]);
        }
    }

    private void RefreshAccountLabels()
    {
        foreach (var room in Workspaces.SelectMany(workspace => workspace.Rooms))
        {
            room.InvalidateModerationPermission();
            room.NotifyDanmuConfigurationAccountChanged();
        }

        foreach (var workspace in Workspaces)
        {
            workspace.RefreshAccountLabel();
        }
    }

    private void RefreshForwardAccountOptions()
    {
        var nextOptions = new List<ForwardAccountOption>
        {
            ForwardAccountOptions.FirstOrDefault(option => option.Account is null) ?? new()
        };

        foreach (var account in Accounts)
        {
            var option = ForwardAccountOptions.FirstOrDefault(option => ReferenceEquals(option.Account, account))
                         ?? new ForwardAccountOption(account);
            nextOptions.Add(option);
        }

        for (var index = ForwardAccountOptions.Count - 1; index >= 0; index--)
        {
            var option = ForwardAccountOptions[index];

            if (!nextOptions.Contains(option))
            {
                ForwardAccountOptions.RemoveAt(index);
                option.Dispose();
            }
        }

        for (var index = 0; index < nextOptions.Count; index++)
        {
            var currentIndex = ForwardAccountOptions.IndexOf(nextOptions[index]);

            if (currentIndex < 0)
            {
                ForwardAccountOptions.Insert(index, nextOptions[index]);
            }
            else if (currentIndex != index)
            {
                ForwardAccountOptions.Move(currentIndex, index);
            }
        }
    }

    private void RefreshThemeModeOptions()
    {
        foreach (var option in ThemeModeOptions)
        {
            option.IsSelected = option.Value == Settings.ThemeMode;
        }
    }

    private void RefreshSettingsSectionOptions()
    {
        foreach (var option in SettingsSectionOptions)
        {
            option.IsSelected = option.Key == selectedSettingsSectionKey;
        }
    }

    private void RefreshShortcutBindingGroups()
    {
        ShortcutBindingGroups.Clear();

        foreach (var group in ShortcutActionCatalog.Actions.GroupBy(action => new
                 {
                     action.GroupKey,
                     action.GroupName
                 }))
        {
            var bindings = group
                .Select(action => new ShortcutBindingViewModel(
                    action,
                    Settings.ShortcutBindings.First(binding => binding.ActionKey == action.ActionKey),
                    OnShortcutBindingChanged))
                .ToList();
            ShortcutBindingGroups.Add(new(group.Key.GroupName, bindings));
        }

        RefreshShortcutConflicts();
    }

    private void OnShortcutBindingChanged(ShortcutBindingViewModel binding)
    {
        RefreshShortcutConflicts();
        _ = SaveAsync();
    }

    private void RefreshShortcutConflicts()
    {
        var conflictActionKeys = ShortcutBindingService.GetConflictActionKeys(Settings.ShortcutBindings);

        foreach (var binding in EnumerateShortcutBindings())
        {
            binding.SetConflicted(conflictActionKeys.Contains(binding.ActionKey));
        }
    }

    private IEnumerable<ShortcutBindingViewModel> EnumerateShortcutBindings()
    {
        return ShortcutBindingGroups.SelectMany(group => group.Bindings);
    }

    private LyricLibraryItem UpsertLyricLibraryItem(LyricLibraryItem item)
    {
        var savedItem = lyricLibraryService.Upsert(State.LyricLibrary, item);

        if (!LyricLibrary.Contains(savedItem))
        {
            LyricLibrary.Add(savedItem);
        }

        RefreshFilteredLyricLibrary();
        StatusMessage = "歌词已写入本地歌词库";
        return savedItem;
    }

    private void UseLyricLibraryItem(LyricLibraryItem item)
    {
        var room = SelectedWorkspace?.SelectedRoom;

        if (room is null)
        {
            return;
        }

        lyricLibraryService.Touch(item);
        room.LoadLyric(item);
        RefreshFilteredLyricLibrary();
        StatusMessage = "歌词已加载到当前直播间";
    }

    private void RefreshFilteredLyricLibrary()
    {
        FilteredLyricLibrary.Clear();

        foreach (var item in lyricLibraryService.Search(State.LyricLibrary, LyricLibrarySearchText))
        {
            FilteredLyricLibrary.Add(item);
        }
    }

    private void OpenDirectory(string directory, string errorPrefix)
    {
        try
        {
            appBackupService.OpenDirectory(directory);
        }
        catch (Exception exception)
        {
            StatusMessage = $"{errorPrefix}：{exception.Message}";
        }
    }

    private void RefreshTreeSelection()
    {
        foreach (var workspace in Workspaces)
        {
            workspace.IsSelected = workspace == SelectedWorkspace;
            workspace.RefreshRoomSelection();
        }
    }
}
