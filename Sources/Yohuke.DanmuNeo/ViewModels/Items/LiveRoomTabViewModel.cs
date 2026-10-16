using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>
/// 直播间标签页视图模型。
/// </summary>
public partial class LiveRoomTabViewModel : ViewModelBase
{
    private const double MIN_LYRIC_PLAYBACK_RATE = 0.25;
    private const double MAX_LYRIC_PLAYBACK_RATE = 3.0;
    private const double DEFAULT_LYRIC_PLAYBACK_RATE = 1.0;
    private const double LYRIC_PLAYBACK_RATE_STEP = 0.1;
    private const double LYRIC_SEEK_STEP_SECONDS = 0.5;
    private const double DEFAULT_LAST_LYRIC_DURATION_SECONDS = 3.0;
    private const int LYRIC_PLAYBACK_TICK_MS = 50;
    private const double MIN_LIVE_PLAYER_VOLUME_PERCENT = 0;
    private const double MAX_LIVE_PLAYER_VOLUME_PERCENT = 100;
    private const int MAX_RECENT_SUPER_CHAT_IDENTITIES = 512;
    private const int MAX_RECENT_GIFT_COMBOS = 300;
    private const string DEFAULT_SUPER_CHAT_COLOR = "#2A60B2";

    private CancellationTokenSource? listenTokenSource;
    private ILiveDanmuSocket? activeListenSocket;
    private CancellationTokenSource? lyricTokenSource;
    private readonly Dictionary<string, CancellationTokenSource> forwardTokenSources = [];
    private readonly HashSet<string> recentSuperChatIdentities = new(StringComparer.Ordinal);
    private readonly Queue<string> recentSuperChatIdentityOrder = new();
    private readonly HashSet<string> recentGuardPurchaseIdentities = new(StringComparer.Ordinal);
    private readonly Queue<string> recentGuardPurchaseIdentityOrder = new();
    private readonly Dictionary<string, (DanmuFeedItem Item, int Count, decimal UnitPrice, LinkedListNode<string> OrderNode)> giftComboItems = new(StringComparer.Ordinal);
    private readonly Dictionary<DanmuFeedItem, string> giftComboKeysByItem = new();
    private readonly LinkedList<string> giftComboOrder = new();
    private Func<LiveRoomTabViewModel, BilibiliAccount?>? resolveAccount;
    private Func<string, string, BilibiliAccount?>? resolveAccountByRoom;
    private Func<string, BilibiliAccount?>? resolveAccountById;
    private Func<AppSettings>? getSettings;
    private Func<MarkSymbolGroup>? getMarkSymbolGroup;
    private Func<IEnumerable<MarkSymbolGroup>>? getSymbolGroups;
    private Func<LiveRoomTabViewModel, IEnumerable<ForwardSourceRoomOption>>? getForwardSourceRooms;
    private IReadOnlyList<ForwardAccountOption> forwardAccounts = [];
    private IBilibiliUserNameService? userNameService;
    private Func<Task>? saveState;
    private IDanmuSendService? sendService;
    private IRoomAvatarService? avatarCacheService;
    private IEmoticonCacheService? emoticonCacheService;
    private IBilibiliLiveStreamService? liveStreamService;
    private ILivePlayerService? livePlayerService;
    private ILiveDanmuSocketFactory liveDanmuSocketFactory = new BilibiliLiveDanmuSocketFactory();
    private Bitmap? avatarImage;
    private string? livePlayerToken;
    private bool suppressLiveQualityChange;
    private DateTimeOffset lyricPlaybackStartedAt;
    private double lyricPlaybackStartPositionSeconds;
    private int lastAutoEnteredLyricIndex = -1;
    private readonly ConcurrentDictionary<LyricLineState, byte> lyricLinesSending = new();
    private long draftSendOperationId;
    private long draftRevision;
    private readonly DanmuForwardService forwardService = new();
    private readonly LyricTimelineService lyricTimelineService = new();
    private readonly TranslateHistoryExportService translateHistoryExportService = new();

    /// <summary>
    /// 初始化直播间标签页视图模型。
    /// </summary>
    public LiveRoomTabViewModel(LiveRoomTabState state)
    {
        State = state;
        inputDraft = state.InputDraft;
        lyricInput = state.LyricText;
        translatedLyricInput = state.TranslatedLyricText;
        lyricTitle = state.LyricTitle;
        lyricPlaybackRate = NormalizeLyricPlaybackRate(state.LyricPlaybackRate);
        state.LyricPlaybackRate = lyricPlaybackRate;
        lyricSendMode = NormalizeLyricSendMode(state.LyricSendMode);
        state.LyricSendMode = lyricSendMode;
        preventRepeatedLyricSend = state.PreventRepeatedLyricSend;
        livePlayerVolumePercent = NormalizeLivePlayerVolumePercent(state.LivePlayerVolumePercent);
        state.LivePlayerVolumePercent = livePlayerVolumePercent;
        isLivePlayerMuted = state.IsLivePlayerMuted;
        avatarPath = state.AvatarPath;
        RefreshAvatarImage();
        ForwardRules = new(state.ForwardRules.Select(CreateForwardRuleViewModel));
    }

    /// <summary>
    /// 直播间状态。
    /// </summary>
    public LiveRoomTabState State { get; }

    /// <summary>
    /// 弹幕列表。
    /// </summary>
    public ObservableCollection<DanmuFeedItem> DanmuItems { get; } = [];

    /// <summary>
    /// SC 与上舰记录。
    /// </summary>
    public ObservableCollection<SuperChatItem> SuperChats { get; } = [];

    /// <summary>
    /// 同传发送历史。
    /// </summary>
    public ObservableCollection<DanmuFeedItem> TranslateHistoryItems { get; } = [];

    /// <summary>
    /// 歌词行。
    /// </summary>
    public ObservableCollection<LyricLineState> Lyrics { get; } = [];

    /// <summary>
    /// 歌词发送内容模式选项。
    /// </summary>
    public IReadOnlyList<LyricSendModeOption> LyricSendModeOptions { get; } =
    [
        new(LyricSendMode.Bilingual, "原文 + 翻译"),
        new(LyricSendMode.OriginalOnly, "仅原文"),
        new(LyricSendMode.TranslationOnly, "仅翻译")
    ];

    /// <summary>
    /// 转发规则。
    /// </summary>
    public ObservableCollection<DanmuForwardRuleViewModel> ForwardRules { get; }

    /// <summary>
    /// 可选转发源直播间。
    /// </summary>
    public ObservableCollection<ForwardSourceRoomOption> ForwardSourceRooms { get; } = [];

    /// <summary>
    /// 直播清晰度选项。
    /// </summary>
    public ObservableCollection<LiveQualityOption> LiveQualityOptions { get; } = [];

    /// <summary>
    /// 标签页 ID。
    /// </summary>
    public string Id => State.Id;

    /// <summary>
    /// 直播间 ID。
    /// </summary>
    public string RoomId
    {
        get => State.RoomId;
        set
        {
            if (State.RoomId == value)
            {
                return;
            }

            StopListening();
            State.RoomId = value;
            NotifyDanmuConfigurationAccountChanged();
            InvalidateDraftSendFeedback();
            AvatarPath = "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(Header));
            OnPropertyChanged(nameof(AvatarPlaceholderBrush));
            OnPropertyChanged(nameof(AvatarPlaceholderText));
        }
    }

    /// <summary>
    /// 直播间名称。
    /// </summary>
    public string RoomName
    {
        get => State.RoomName;
        set
        {
            if (State.RoomName == value)
            {
                return;
            }

            State.RoomName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Header));
            OnPropertyChanged(nameof(AvatarPlaceholderText));
        }
    }

    /// <summary>
    /// 主播 UID。
    /// </summary>
    public string OwnerUid
    {
        get => State.OwnerUid;
        set
        {
            if (State.OwnerUid == value)
            {
                return;
            }

            State.OwnerUid = value;
            AvatarPath = "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvatarPlaceholderBrush));
        }
    }

    /// <summary>
    /// 标签标题。
    /// </summary>
    public string Header => string.IsNullOrWhiteSpace(RoomName) ? RoomId : RoomName;

    /// <summary>
    /// 账号覆盖 ID。
    /// </summary>
    public string? AccountOverrideId
    {
        get => State.AccountOverrideId;
        set
        {
            if (State.AccountOverrideId == value)
            {
                return;
            }

            State.AccountOverrideId = value;
            InvalidateModerationPermission();
            NotifyDanmuConfigurationAccountChanged();
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private string inputDraft = "";

    [ObservableProperty] private string draftSendStatus = "";

    [ObservableProperty] private bool isDraftSendFailed;

    [ObservableProperty] private string lyricInput = "";

    [ObservableProperty] private string translatedLyricInput = "";

    [ObservableProperty] private string lyricTitle = "";

    [ObservableProperty] private double lyricPlaybackRate = DEFAULT_LYRIC_PLAYBACK_RATE;

    [ObservableProperty] private LyricSendMode lyricSendMode = LyricSendMode.Bilingual;

    [ObservableProperty] private bool preventRepeatedLyricSend = true;

    [ObservableProperty] private string connectionStatus = "未连接";

    [ObservableProperty] private bool isListening;

    [ObservableProperty] private bool isLyricAutoSending;

    [ObservableProperty] private string avatarPath = "";

    [ObservableProperty] private bool isLivePlayerVisible;

    [ObservableProperty] private string livePlayerUrl = "";

    [ObservableProperty] private double livePlayerVolumePercent;

    [ObservableProperty] private bool isLivePlayerMuted;

    [ObservableProperty] private LiveQualityOption? selectedLiveQuality;

    /// <summary>
    /// 是否未监听。
    /// </summary>
    public bool IsNotListening => !IsListening;

    /// <summary>
    /// 歌词自动发送是否已暂停。
    /// </summary>
    public bool IsLyricAutoSendingPaused => !IsLyricAutoSending;

    /// <summary>
    /// 歌词自动发送切换按钮提示。
    /// </summary>
    public string LyricAutoSendingToggleToolTip => IsLyricAutoSending ? "暂停歌词发送" : "开始歌词发送";

    /// <summary>
    /// 监听切换按钮文本。
    /// </summary>
    public string ListenToggleText => IsListening ? "停止" : "监听";

    /// <summary>
    /// 是否存在主播头像。
    /// </summary>
    public bool HasAvatar => avatarImage is not null;

    /// <summary>
    /// 是否没有主播头像。
    /// </summary>
    public bool HasNoAvatar => !HasAvatar;

    /// <summary>
    /// 是否隐藏直播播放器。
    /// </summary>
    public bool IsLivePlayerHidden => !IsLivePlayerVisible;

    /// <summary>
    /// 直播播放器播放按钮提示。
    /// </summary>
    public string LivePlayerToggleToolTip => IsLivePlayerVisible ? "停止播放" : "播放直播";

    /// <summary>
    /// 直播播放器是否未静音。
    /// </summary>
    public bool IsLivePlayerAudible => !IsLivePlayerMuted;

    /// <summary>
    /// 直播播放器音量文本。
    /// </summary>
    public string LivePlayerVolumeText => $"{Math.Round(LivePlayerVolumePercent)}%";

    /// <summary>
    /// 主播头像位图。
    /// </summary>
    public Bitmap? AvatarImage => avatarImage;

    /// <summary>
    /// 头像占位文字。
    /// </summary>
    public string AvatarPlaceholderText
    {
        get
        {
            var source = string.IsNullOrWhiteSpace(RoomName) ? RoomId : RoomName;
            var character = source.Trim().FirstOrDefault();
            return character == default ? "?" : character.ToString().ToUpperInvariant();
        }
    }

    /// <summary>
    /// 头像占位背景。
    /// </summary>
    public IBrush AvatarPlaceholderBrush => new SolidColorBrush(CreateAvatarPlaceholderColor(GetAvatarIdentity()));

    partial void OnIsListeningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotListening));
        OnPropertyChanged(nameof(ListenToggleText));
    }

    partial void OnIsLyricAutoSendingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLyricAutoSendingPaused));
        OnPropertyChanged(nameof(LyricAutoSendingToggleToolTip));
    }

    partial void OnIsLivePlayerVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLivePlayerHidden));
        OnPropertyChanged(nameof(LivePlayerToggleToolTip));
    }

    partial void OnLivePlayerVolumePercentChanged(double value)
    {
        var normalizedValue = NormalizeLivePlayerVolumePercent(value);

        if (ShouldNormalizeLivePlayerVolumePercent(value, normalizedValue))
        {
            LivePlayerVolumePercent = normalizedValue;
            return;
        }

        State.LivePlayerVolumePercent = normalizedValue;
        OnPropertyChanged(nameof(LivePlayerVolumeText));
    }

    partial void OnIsLivePlayerMutedChanged(bool value)
    {
        State.IsLivePlayerMuted = value;
        OnPropertyChanged(nameof(IsLivePlayerAudible));
    }

    partial void OnSelectedLiveQualityChanged(LiveQualityOption? value)
    {
        if (suppressLiveQualityChange || !IsLivePlayerVisible)
        {
            return;
        }

        _ = RefreshLivePlayerAsync();
    }

    [ObservableProperty] private bool isSelected;

    [ObservableProperty] private int activeLyricIndex;

    [ObservableProperty] private LyricLineState? activeLyricLine;

    [ObservableProperty] private double lyricPlaybackPositionSeconds;

    partial void OnInputDraftChanged(string value)
    {
        State.InputDraft = value;
        InvalidateDraftSendFeedback();
        OnPropertyChanged(nameof(IsInputDraftEmpty));
    }

    /// <summary>
    /// 同传输入是否为空。
    /// </summary>
    public bool IsInputDraftEmpty => string.IsNullOrWhiteSpace(InputDraft);

    /// <summary>
    /// 当前歌词标题展示文本。
    /// </summary>
    public string CurrentLyricTitleText => HasLyricTitle ? LyricTitle : "未选择歌词";

    /// <summary>
    /// 当前是否存在可复制的歌词标题。
    /// </summary>
    public bool HasLyricTitle => !string.IsNullOrWhiteSpace(LyricTitle);

    /// <summary>
    /// 是否存在同传历史。
    /// </summary>
    public bool HasTranslateHistory => TranslateHistoryItems.Count > 0;

    partial void OnLyricInputChanged(string value)
    {
        State.LyricText = value;
    }

    partial void OnTranslatedLyricInputChanged(string value)
    {
        State.TranslatedLyricText = value;
    }

    partial void OnLyricTitleChanged(string value)
    {
        State.LyricTitle = value;
        OnPropertyChanged(nameof(CurrentLyricTitleText));
        OnPropertyChanged(nameof(HasLyricTitle));
    }

    partial void OnLyricPlaybackRateChanged(double value)
    {
        var normalizedValue = NormalizeLyricPlaybackRate(value);

        if (Math.Abs(normalizedValue - value) > double.Epsilon)
        {
            LyricPlaybackRate = normalizedValue;
            return;
        }

        State.LyricPlaybackRate = normalizedValue;
        ResetLyricPlaybackClock(LyricPlaybackPositionSeconds);
    }

    partial void OnLyricSendModeChanged(LyricSendMode value)
    {
        var normalizedValue = NormalizeLyricSendMode(value);

        if (normalizedValue != value)
        {
            LyricSendMode = normalizedValue;
            return;
        }

        State.LyricSendMode = normalizedValue;
        SeekNextLyricCommand.NotifyCanExecuteChanged();
    }

    partial void OnPreventRepeatedLyricSendChanged(bool value)
    {
        State.PreventRepeatedLyricSend = value;
    }

    partial void OnActiveLyricIndexChanged(int value)
    {
        RefreshActiveLyric();
    }

    partial void OnAvatarPathChanged(string value)
    {
        State.AvatarPath = value;
        RefreshAvatarImage();
        OnPropertyChanged(nameof(HasAvatar));
        OnPropertyChanged(nameof(HasNoAvatar));
    }

    /// <summary>
    /// 注入运行时服务。
    /// </summary>
    public void Configure(
        Func<LiveRoomTabViewModel, BilibiliAccount?> resolveAccount,
        Func<string, string, BilibiliAccount?> resolveAccountByRoom,
        Func<AppSettings> getSettings,
        Func<MarkSymbolGroup> getMarkSymbolGroup,
        Func<IEnumerable<MarkSymbolGroup>> getSymbolGroups,
        Func<LiveRoomTabViewModel, IEnumerable<ForwardSourceRoomOption>> getForwardSourceRooms,
        IDanmuSendService sendService,
        IRoomAvatarService avatarCacheService,
        Func<Task> saveState,
        IBilibiliLiveStreamService? liveStreamService = null,
        ILivePlayerService? livePlayerService = null,
        Func<string, BilibiliAccount?>? resolveAccountById = null,
        ILiveDanmuSocketFactory? liveDanmuSocketFactory = null,
        IBilibiliModerationService? moderationService = null,
        IReadOnlyList<ForwardAccountOption>? forwardAccountOptions = null,
        IBilibiliUserNameService? userNameService = null,
        IBilibiliDanmuConfigService? danmuConfigService = null,
        IEmoticonCacheService? emoticonCacheService = null)
    {
        var wasConfigurationActive = isDanmuConfigurationActive;

        if (wasConfigurationActive)
        {
            DeactivateDanmuConfiguration();
        }

        this.resolveAccount = resolveAccount;
        this.resolveAccountByRoom = resolveAccountByRoom;
        this.getSettings = getSettings;
        this.getMarkSymbolGroup = getMarkSymbolGroup;
        this.getSymbolGroups = getSymbolGroups;
        this.getForwardSourceRooms = getForwardSourceRooms;
        forwardAccounts = forwardAccountOptions ?? forwardAccounts;
        this.userNameService = userNameService ?? this.userNameService;
        this.danmuConfigService = danmuConfigService ?? this.danmuConfigService ?? new BilibiliDanmuConfigService();
        this.sendService = sendService;
        this.avatarCacheService = avatarCacheService;
        this.emoticonCacheService = emoticonCacheService ?? this.emoticonCacheService;
        this.saveState = saveState;
        this.liveStreamService = liveStreamService;
        this.livePlayerService = livePlayerService;
        this.resolveAccountById = resolveAccountById;
        this.liveDanmuSocketFactory = liveDanmuSocketFactory ?? new BilibiliLiveDanmuSocketFactory();
        this.moderationService = moderationService ?? new BilibiliModerationService();
        foreach (var rule in ForwardRules)
        {
            ConfigureForwardRule(rule);
        }

        sendService.RecordCreated += OnSendRecordCreated;
        ApplyLyric();

        if (wasConfigurationActive)
        {
            _ = ActivateDanmuConfigurationAsync();
        }
    }

    /// <summary>
    /// 加载主播头像。
    /// </summary>
    public async Task<bool> LoadAvatarAsync(bool force = false)
    {
        if (avatarCacheService is null)
        {
            return false;
        }

        if (!force && HasAvatar)
        {
            OnPropertyChanged(nameof(HasAvatar));
            OnPropertyChanged(nameof(HasNoAvatar));
            return true;
        }

        var account = resolveAccount?.Invoke(this);
        var result = await avatarCacheService.GetRoomAvatarResultAsync(RoomId, OwnerUid, account?.Cookie);

        if (string.IsNullOrWhiteSpace(result.AvatarPath))
        {
            return false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrWhiteSpace(result.OwnerUid) && OwnerUid != result.OwnerUid)
            {
                State.OwnerUid = result.OwnerUid;
                OnPropertyChanged(nameof(OwnerUid));
                OnPropertyChanged(nameof(AvatarPlaceholderBrush));
            }

            if ((string.IsNullOrWhiteSpace(RoomName) || RoomName == RoomId) &&
                !string.IsNullOrWhiteSpace(result.OwnerName))
            {
                RoomName = result.OwnerName;
            }

            AvatarPath = result.AvatarPath;
        });

        return true;
    }

    private void RefreshAvatarImage()
    {
        avatarImage?.Dispose();
        avatarImage = null;

        if (string.IsNullOrWhiteSpace(AvatarPath) || !File.Exists(AvatarPath))
        {
            OnPropertyChanged(nameof(AvatarImage));
            return;
        }

        try
        {
            avatarImage = new(AvatarPath);
        }
        catch
        {
            avatarImage = null;
        }

        OnPropertyChanged(nameof(AvatarImage));
    }

    /// <summary>
    /// 根据直播间 ID 生成稳定头像占位颜色。
    /// </summary>
    public static Color CreateAvatarPlaceholderColor(string roomId)
    {
        var hash = 0;

        foreach (var character in roomId)
        {
            hash = unchecked((hash * 31) + character);
        }

        var hue = Math.Abs(hash) % 360;
        return FromHsl(hue, 0.52, 0.46);
    }

    private string GetAvatarIdentity()
    {
        return string.IsNullOrWhiteSpace(OwnerUid) ? RoomId : OwnerUid;
    }

    private static Color FromHsl(int hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var hueSection = hue / 60.0;
        var x = chroma * (1 - Math.Abs((hueSection % 2) - 1));
        var match = lightness - (chroma / 2);
        (double Red, double Green, double Blue) color = hueSection switch
        {
            >= 0 and < 1 => (chroma, x, 0),
            >= 1 and < 2 => (x, chroma, 0),
            >= 2 and < 3 => (0, chroma, x),
            >= 3 and < 4 => (0, x, chroma),
            >= 4 and < 5 => (x, 0, chroma),
            _ => (chroma, 0, x)
        };

        return Color.FromRgb(
            ToByte(color.Red + match),
            ToByte(color.Green + match),
            ToByte(color.Blue + match));
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    }

    /// <summary>
    /// 解析歌词输入。
    /// </summary>
    [RelayCommand]
    public void ApplyLyric()
    {
        StopAutoLyric(false);
        Lyrics.Clear();

        foreach (var line in lyricTimelineService.ParseMultilingual(LyricInput, TranslatedLyricInput))
        {
            Lyrics.Add(line);
        }

        RefreshLyricDurations();
        ResetLyricSession();
    }

    /// <summary>
    /// 清空当前歌词。
    /// </summary>
    [RelayCommand]
    public void ClearLyric()
    {
        StopAutoLyric(false);
        LyricTitle = "";
        LyricInput = "";
        TranslatedLyricInput = "";
        Lyrics.Clear();
        ResetLyricSession();
    }

    /// <summary>
    /// 发送同传输入。
    /// </summary>
    [RelayCommand]
    public async Task SendDraftAsync()
    {
        var operationId = Interlocked.Increment(ref draftSendOperationId);
        IsDraftSendFailed = false;

        if (string.IsNullOrWhiteSpace(InputDraft))
        {
            return;
        }

        var revision = draftRevision;
        var settings = getSettings?.Invoke();
        var account = resolveAccount?.Invoke(this);

        if (settings is null || sendService is null)
        {
            TrySetDraftSendFeedback(operationId, revision, "发送失败：发送服务不可用", true);
            return;
        }

        var markGroup = getMarkSymbolGroup?.Invoke() ?? MarkSymbolService.CreateDefaultGroup(settings);
        var content = ShieldReplacementService.Apply(InputDraft.Trim(), settings, false);
        var message = $"{markGroup.TranslateOpenMark}{content}{markGroup.TranslateCloseMark}";
        DraftSendStatus = "发送中";
        var results = await SendMessageAsync(message, account, settings);
        var acceptedResults = results.Where(result => result.IsAccepted).ToList();
        var failedResults = results.Where(result => !result.IsAccepted).ToList();

        if (results.Count == 0)
        {
            TrySetDraftSendFeedback(operationId, revision, "发送失败：发送服务不可用", true);
            return;
        }

        if (acceptedResults.Count == 0)
        {
            TrySetDraftSendFeedback(operationId, revision, $"发送失败：{failedResults[0].ErrorMessage}", true);
            return;
        }

        if (failedResults.Count > 0)
        {
            TrySetDraftSendFeedback(operationId, revision,
                $"部分发送失败：{acceptedResults.Count}/{results.Count} 段接口已接受", true);
            return;
        }

        if (TrySetDraftSendFeedback(operationId, revision, "接口已接受", false))
        {
            InputDraft = "";
        }
    }

    /// <summary>
    /// 清空同传输入。
    /// </summary>
    [RelayCommand]
    public void ClearInputDraft()
    {
        InputDraft = "";
    }

    /// <summary>
    /// 清空同传历史。
    /// </summary>
    [RelayCommand]
    public void ClearTranslateHistory()
    {
        TranslateHistoryItems.Clear();
        OnPropertyChanged(nameof(HasTranslateHistory));
    }

    /// <summary>
    /// 导出同传历史。
    /// </summary>
    [RelayCommand]
    public async Task ExportTranslateHistoryAsync()
    {
        if (TranslateHistoryItems.Count == 0)
        {
            return;
        }

        var outputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        await translateHistoryExportService.ExportAsync(
            TranslateHistoryItems,
            outputDirectory,
            $"translate-history-{Header}");
    }

    /// <summary>
    /// 导出同传历史到指定文件。
    /// </summary>
    public async Task ExportTranslateHistoryToFileAsync(string filePath)
    {
        if (TranslateHistoryItems.Count == 0)
        {
            return;
        }

        await translateHistoryExportService.ExportToFileAsync(TranslateHistoryItems, filePath);
    }

    /// <summary>
    /// 手动发送当前歌词，略过空行并推进到下一条非空歌词。
    /// </summary>
    [RelayCommand]
    public async Task SendCurrentLyricAsync()
    {
        if (Lyrics.Count == 0)
        {
            return;
        }

        var index = FindNextSendableLyricIndex(Math.Clamp(ActiveLyricIndex, 0, Lyrics.Count - 1));

        if (index < 0)
        {
            return;
        }

        var line = Lyrics[index];
        await SendLyricLineAsync(line, true);
        AdvanceToNextSendableLyric(line);
    }

    /// <summary>
    /// 跳转到下一条非空歌词并发送，保留当前播放或暂停状态。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSeekNextLyric))]
    public async Task SeekNextLyricAsync()
    {
        if (!CanSeekNextLyric())
        {
            return;
        }

        await SeekAndSendLyricLineAsync(Lyrics[ActiveLyricIndex + 1]);
    }

    private bool CanSeekNextLyric()
    {
        return ActiveLyricIndex >= 0 && FindNextSendableLyricIndex(ActiveLyricIndex + 1) >= 0;
    }

    /// <summary>
    /// 定位并发送指定歌词行，空行向后跳过，保留播放状态并遵从防重复设置。
    /// </summary>
    public async Task SeekAndSendLyricLineAsync(LyricLineState? line)
    {
        if (line is null)
        {
            return;
        }

        var index = Lyrics.IndexOf(line);

        if (index < 0)
        {
            return;
        }

        index = FindNextSendableLyricIndex(index);

        if (index < 0)
        {
            return;
        }

        line = Lyrics[index];
        var positionSeconds = line.TimeSeconds >= 0 ? line.TimeSeconds : LyricPlaybackPositionSeconds;
        SetLyricPlaybackPosition(positionSeconds, index);
        lastAutoEnteredLyricIndex = index;
        await SendLyricLineAsync(line, false);
    }

    /// <summary>
    /// 快退歌词播放位置。
    /// </summary>
    [RelayCommand]
    public void RewindLyric()
    {
        AdjustLyricPlaybackPosition(-LYRIC_SEEK_STEP_SECONDS);
    }

    /// <summary>
    /// 快进歌词播放位置。
    /// </summary>
    [RelayCommand]
    public void FastForwardLyric()
    {
        AdjustLyricPlaybackPosition(LYRIC_SEEK_STEP_SECONDS);
    }

    /// <summary>
    /// 将歌词播放倍率降低 0.1。
    /// </summary>
    [RelayCommand]
    public void DecreaseLyricPlaybackRate()
    {
        AdjustLyricPlaybackRate(-LYRIC_PLAYBACK_RATE_STEP);
    }

    /// <summary>
    /// 将歌词播放倍率提高 0.1。
    /// </summary>
    [RelayCommand]
    public void IncreaseLyricPlaybackRate()
    {
        AdjustLyricPlaybackRate(LYRIC_PLAYBACK_RATE_STEP);
    }

    private void AdjustLyricPlaybackRate(double offset)
    {
        LyricPlaybackRate = Math.Round(
            LyricPlaybackRate + offset,
            2,
            MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 调整歌词播放位置。
    /// </summary>
    public void AdjustLyricPlaybackPosition(double offsetSeconds)
    {
        if (!LyricTimelineService.HasTimeline(Lyrics))
        {
            return;
        }

        SetLyricPlaybackPosition(LyricPlaybackPositionSeconds + offsetSeconds);
    }

    /// <summary>
    /// 定位歌词播放位置到指定歌词行开头。
    /// </summary>
    public void SeekLyricLineStart(LyricLineState? line)
    {
        if (line is null || line.TimeSeconds < 0 || !Lyrics.Contains(line))
        {
            return;
        }

        SetLyricPlaybackPosition(line.TimeSeconds);
    }

    /// <summary>
    /// 开始或暂停歌词自动发送。
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ToggleAutoLyricAsync()
    {
        if (IsLyricAutoSending)
        {
            lyricTokenSource?.Cancel();
            IsLyricAutoSending = false;
            return;
        }

        await StartAutoLyricAsync();
    }

    /// <summary>
    /// 开始歌词自动发送。
    /// </summary>
    [RelayCommand]
    public async Task StartAutoLyricAsync()
    {
        if (IsLyricAutoSending)
        {
            return;
        }

        if (!LyricTimelineService.HasTimeline(Lyrics))
        {
            ApplyLyric();
        }

        if (!LyricTimelineService.HasTimeline(Lyrics))
        {
            return;
        }

        var tokenSource = new CancellationTokenSource();
        lyricTokenSource = tokenSource;
        IsLyricAutoSending = true;
        await RunAutoLyricAsync(tokenSource);
    }

    /// <summary>
    /// 停止歌词自动发送。
    /// </summary>
    [RelayCommand]
    public void StopAutoLyric()
    {
        StopAutoLyric(true);
    }

    private void StopAutoLyric(bool resetPosition)
    {
        lyricTokenSource?.Cancel();
        IsLyricAutoSending = false;

        if (resetPosition)
        {
            SetLyricPlaybackPosition(GetFirstTimedLyricPosition());
        }
    }

    private void RefreshLyricDurations()
    {
        var timedLines = Lyrics.Where(line => line.TimeSeconds >= 0).ToList();

        for (var index = 0; index < Lyrics.Count; index++)
        {
            Lyrics[index].DurationSeconds = 0;
        }

        for (var index = 0; index < timedLines.Count; index++)
        {
            var line = timedLines[index];
            var duration = index < timedLines.Count - 1
                ? timedLines[index + 1].TimeSeconds - line.TimeSeconds
                : DEFAULT_LAST_LYRIC_DURATION_SECONDS;
            line.DurationSeconds = Math.Max(0.1, duration);
        }
    }

    private void ResetLyricSession()
    {
        lastAutoEnteredLyricIndex = -1;

        foreach (var line in Lyrics)
        {
            line.IsSent = false;
            line.Progress = 0;
        }

        SetLyricPlaybackPosition(GetFirstTimedLyricPosition());
    }

    private void SetLyricPlaybackPosition(double positionSeconds, int? targetIndex = null)
    {
        if (Lyrics.Count == 0)
        {
            LyricPlaybackPositionSeconds = 0;
            ActiveLyricLine = null;
            SeekNextLyricCommand.NotifyCanExecuteChanged();
            return;
        }

        var normalizedPosition = Math.Clamp(
            positionSeconds,
            GetFirstTimedLyricPosition(),
            GetLastLyricEndPosition());
        LyricPlaybackPositionSeconds = normalizedPosition;
        ActiveLyricIndex = targetIndex ?? FindActiveLyricIndex(normalizedPosition);
        RefreshActiveLyric();
        UpdateLyricProgress(normalizedPosition);
        ResetLyricPlaybackClock(normalizedPosition);
    }

    private void ResetLyricPlaybackClock(double positionSeconds)
    {
        lyricPlaybackStartPositionSeconds = positionSeconds;
        lyricPlaybackStartedAt = DateTimeOffset.Now;
    }

    private int FindActiveLyricIndex(double positionSeconds)
    {
        var activeIndex = 0;

        for (var index = 0; index < Lyrics.Count; index++)
        {
            var line = Lyrics[index];

            if (line.TimeSeconds < 0)
            {
                continue;
            }

            if (line.TimeSeconds > positionSeconds)
            {
                break;
            }

            activeIndex = index;
        }

        return activeIndex;
    }

    private void UpdateLyricProgress(double positionSeconds)
    {
        for (var index = 0; index < Lyrics.Count; index++)
        {
            var line = Lyrics[index];

            if (index != ActiveLyricIndex || line.TimeSeconds < 0 || line.DurationSeconds <= 0)
            {
                line.Progress = 0;
                continue;
            }

            line.Progress = (positionSeconds - line.TimeSeconds) / line.DurationSeconds;
        }
    }

    private double GetFirstTimedLyricPosition()
    {
        return Lyrics.FirstOrDefault(line => line.TimeSeconds >= 0)?.TimeSeconds ?? 0;
    }

    private double GetLastLyricEndPosition()
    {
        var lastTimedLine = Lyrics.LastOrDefault(line => line.TimeSeconds >= 0);
        return lastTimedLine is null
            ? 0
            : lastTimedLine.TimeSeconds + Math.Max(0.1, lastTimedLine.DurationSeconds);
    }

    private int FindNextSendableLyricIndex(int startIndex)
    {
        var settings = getSettings?.Invoke();

        for (var index = Math.Max(0, startIndex); index < Lyrics.Count; index++)
        {
            var line = Lyrics[index];

            if (LyricTimelineService.CreateSendContents(
                    line.Content, line.TranslatedContent, LyricSendMode, settings).Count > 0)
            {
                return index;
            }
        }

        return -1;
    }

    private void AdvanceToNextSendableLyric(LyricLineState line)
    {
        var index = Lyrics.IndexOf(line);

        if (index < 0)
        {
            return;
        }

        var nextIndex = FindNextSendableLyricIndex(index + 1);

        if (nextIndex < 0)
        {
            SetLyricPlaybackPosition(GetLastLyricEndPosition(), index);
            return;
        }

        var nextLine = Lyrics[nextIndex];
        var positionSeconds = nextLine.TimeSeconds >= 0 ? nextLine.TimeSeconds : LyricPlaybackPositionSeconds;
        SetLyricPlaybackPosition(positionSeconds, nextIndex);
    }

    private async Task<bool> SendLyricLineAsync(
        LyricLineState line,
        bool forceSend,
        CancellationToken cancellationToken = default)
    {
        if (!lyricLinesSending.TryAdd(line, 0))
        {
            return false;
        }

        try
        {
            if (!forceSend && PreventRepeatedLyricSend && line.IsSent)
            {
                return false;
            }

            var settings = getSettings?.Invoke();
            var account = resolveAccount?.Invoke(this);

            if (settings is null || sendService is null)
            {
                return false;
            }

            var markGroup = getMarkSymbolGroup?.Invoke() ?? MarkSymbolService.CreateDefaultGroup(settings);
            var contents = LyricTimelineService.CreateSendContents(
                line.Content, line.TranslatedContent, LyricSendMode, settings);

            if (contents.Count == 0)
            {
                return false;
            }

            var allAccepted = true;

            foreach (var content in contents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var message = LyricTimelineService.CreateMessage(
                    markGroup.LyricOpenMark, markGroup.LyricCloseMark, content);
                var results = await SendMessageAsync(message, account, settings, cancellationToken);
                allAccepted &= results.Count > 0 && results.All(result => result.IsAccepted);
            }

            if (!allAccepted)
            {
                return false;
            }

            line.IsSent = true;
            return true;
        }
        finally
        {
            lyricLinesSending.TryRemove(line, out _);
        }
    }

    private async Task SendActiveLyricIfNeededAsync(CancellationToken cancellationToken)
    {
        if (Lyrics.Count == 0 ||
            ActiveLyricIndex == lastAutoEnteredLyricIndex ||
            ActiveLyricIndex < 0 ||
            ActiveLyricIndex >= Lyrics.Count)
        {
            return;
        }

        var line = Lyrics[ActiveLyricIndex];

        if (line.TimeSeconds < 0)
        {
            return;
        }

        lastAutoEnteredLyricIndex = ActiveLyricIndex;
        await SendLyricLineAsync(line, false, cancellationToken);
    }

    /// <summary>
    /// 开始监听直播间。
    /// </summary>
    [RelayCommand]
    public void StartListening()
    {
        if (IsListening)
        {
            return;
        }

        var account = resolveAccount?.Invoke(this);

        if (account is null || string.IsNullOrWhiteSpace(account.Cookie) || string.IsNullOrWhiteSpace(RoomId))
        {
            ConnectionStatus = "缺少账号或房间";
            return;
        }

        if (!long.TryParse(RoomId, out var parsedRoomId) || parsedRoomId <= 0)
        {
            ConnectionStatus = "房间号格式错误";
            return;
        }

        var socket = liveDanmuSocketFactory.Create(parsedRoomId, account.Cookie);
        var tokenSource = new CancellationTokenSource();
        ClearGiftComboState();
        listenTokenSource = tokenSource;
        activeListenSocket = socket;
        socket.DanmuReceived += OnDanmuReceived;
        socket.GiftReceived += OnGiftReceived;
        socket.SuperChatReceived += OnSuperChatReceived;
        socket.GuardPurchaseReceived += OnGuardPurchaseReceived;
        EventHandler onDisconnected = (_, _) => PostListenerConnectionStatus(socket, "连接中断，正在重连");
        EventHandler onRecovered = (_, _) => PostListenerConnectionStatus(socket, "监听中");
        EventHandler<Exception> onError = (_, _) => PostListenerConnectionStatus(socket, "连接中断，正在重连");
        socket.Disconnected += onDisconnected;
        socket.Recovered += onRecovered;
        socket.ErrorReceived += onError;
        IsListening = true;
        ConnectionStatus = "监听中";

        _ = Task.Run((Func<Task?>)(async () =>
        {
            try
            {
                await socket.StartAsync(tokenSource.Token);
            }
            catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
            {
            }
            finally
            {
                socket.DanmuReceived -= OnDanmuReceived;
                socket.GiftReceived -= OnGiftReceived;
                socket.SuperChatReceived -= OnSuperChatReceived;
                socket.GuardPurchaseReceived -= OnGuardPurchaseReceived;
                socket.Disconnected -= onDisconnected;
                socket.Recovered -= onRecovered;
                socket.ErrorReceived -= onError;
                if (socket is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(listenTokenSource, tokenSource))
                    {
                        listenTokenSource = null;
                        activeListenSocket = null;
                        IsListening = false;
                        ConnectionStatus = "未连接";
                        ClearGiftComboState();
                    }

                    tokenSource.Dispose();
                });
            }
        }));
    }

    /// <summary>
    /// 停止监听直播间。
    /// </summary>
    [RelayCommand]
    public void StopListening()
    {
        InvalidateModerationPermission();
        activeListenSocket = null;
        ClearGiftComboState();
        var tokenSource = listenTokenSource;
        listenTokenSource = null;
        tokenSource?.Cancel();
        IsListening = false;
        ConnectionStatus = "未连接";
    }

    private void PostListenerConnectionStatus(ILiveDanmuSocket socket, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(socket, activeListenSocket))
            {
                ConnectionStatus = status;
            }
        });
    }

    /// <summary>
    /// 切换直播间监听状态。
    /// </summary>
    [RelayCommand]
    public void ToggleListening()
    {
        if (IsListening)
        {
            StopListening();
            return;
        }

        StartListening();
    }

    /// <summary>
    /// 添加转发规则。
    /// </summary>
    [RelayCommand]
    public async Task AddForwardRuleAsync()
    {
        RefreshForwardSourceRooms();

        var ruleState = new DanmuForwardRuleState
        {
            MarkSymbolGroupId = getMarkSymbolGroup?.Invoke().Id ?? ""
        };
        var source = ForwardSourceRooms.FirstOrDefault();

        if (source is not null)
        {
            ruleState.SourceWorkspaceId = source.WorkspaceId;
            ruleState.SourceRoomStateId = source.RoomStateId;
        }

        await AddForwardRuleStateAsync(ruleState);
    }

    /// <summary>
    /// 添加指定转发规则。
    /// </summary>
    public async Task AddForwardRuleStateAsync(DanmuForwardRuleState ruleState)
    {
        if (string.IsNullOrWhiteSpace(ruleState.MarkSymbolGroupId))
        {
            ruleState.MarkSymbolGroupId = getMarkSymbolGroup?.Invoke().Id ?? "";
        }

        State.ForwardRules.Add(ruleState);
        var rule = CreateForwardRuleViewModel(ruleState);
        ForwardRules.Add(rule);
        RefreshForwardSourceRooms();

        if (rule.IsEnabled)
        {
            StartForwardRule(rule);
        }
        else
        {
            SetForwardRuleStatus(rule, "未启用");
        }

        await SaveStateAsync();
    }

    /// <summary>
    /// 删除转发规则。
    /// </summary>
    [RelayCommand]
    public async Task RemoveForwardRuleAsync(DanmuForwardRuleViewModel? rule)
    {
        if (rule is null)
        {
            return;
        }

        StopForwardRule(rule);
        rule.Dispose();
        State.ForwardRules.Remove(rule.State);
        ForwardRules.Remove(rule);
        await SaveStateAsync();
    }

    /// <summary>
    /// 刷新转发源直播间选项。
    /// </summary>
    public void RefreshForwardSourceRooms()
    {
        if (getForwardSourceRooms is null)
        {
            ForwardSourceRooms.Clear();
            return;
        }

        var nextSources = getForwardSourceRooms(this).ToList();

        if (ForwardSourceRooms.Select(source => source.Key).SequenceEqual(nextSources.Select(source => source.Key)))
        {
            return;
        }

        ForwardSourceRooms.Clear();

        foreach (var source in nextSources)
        {
            ForwardSourceRooms.Add(source);
        }
    }

    /// <summary>
    /// 停止所有转发监听。
    /// </summary>
    public void StopForwarding()
    {
        foreach (var rule in ForwardRules)
        {
            StopForwardRule(rule);
        }
    }

    /// <summary>
    /// 停止转发并释放规则拥有的后台查询。
    /// </summary>
    public void DisposeForwardRules()
    {
        foreach (var rule in ForwardRules)
        {
            StopForwardRule(rule);
            rule.Dispose();
        }
    }

    /// <summary>
    /// 切换直播播放器播放状态。
    /// </summary>
    [RelayCommand]
    public async Task ToggleLivePlayerAsync()
    {
        if (IsLivePlayerVisible)
        {
            StopLivePlayer();
            return;
        }

        await StartLivePlayerAsync(false);
    }

    /// <summary>
    /// 开始播放直播。
    /// </summary>
    [RelayCommand]
    public async Task PlayLiveAsync()
    {
        await StartLivePlayerAsync(false);
    }

    /// <summary>
    /// 刷新直播播放器。
    /// </summary>
    [RelayCommand]
    public async Task RefreshLivePlayerAsync()
    {
        await StartLivePlayerAsync(false);
    }

    /// <summary>
    /// 追到最新直播流。
    /// </summary>
    [RelayCommand]
    public async Task ChaseLiveAsync()
    {
        await StartLivePlayerAsync(true);
    }

    /// <summary>
    /// 刷新直播清晰度。
    /// </summary>
    [RelayCommand]
    public async Task RefreshLiveQualitiesAsync()
    {
        await RefreshLiveSourceAsync(false);
    }

    /// <summary>
    /// 停止直播播放。
    /// </summary>
    [RelayCommand]
    public void StopLivePlayer()
    {
        livePlayerService?.Revoke(livePlayerToken);
        livePlayerToken = null;
        LivePlayerUrl = "";
        IsLivePlayerVisible = false;
    }

    /// <summary>
    /// 切换直播播放器静音状态。
    /// </summary>
    [RelayCommand]
    public void ToggleLivePlayerMuted()
    {
        IsLivePlayerMuted = !IsLivePlayerMuted;
    }

    /// <summary>
    /// 将弹幕内容插入同传输入框。
    /// </summary>
    public int InsertDanmuContent(string? content, int? insertIndex = null)
    {
        if (content is null)
        {
            return InputDraft.Length;
        }

        var settings = getSettings?.Invoke() ?? new();
        var draft = InputDraft;
        var normalizedInsertIndex = insertIndex ?? draft.Length;

        if (normalizedInsertIndex < 0 || normalizedInsertIndex > draft.Length)
        {
            normalizedInsertIndex = draft.Length;
        }

        var insertText = $"{settings.DanmuInsertOpenMark}{content}{settings.DanmuInsertCloseMark}";
        InputDraft = draft.Insert(normalizedInsertIndex, insertText);

        return normalizedInsertIndex + insertText.Length;
    }

    /// <summary>
    /// 将 SC 内容插入同传输入框。
    /// </summary>
    [RelayCommand]
    public void CopySuperChat(SuperChatItem? superChat)
    {
        if (superChat is null || !superChat.CanInsertContent)
        {
            return;
        }

        InsertDanmuContent(superChat.Content);
    }

    private async Task StartLivePlayerAsync(bool isChasing)
    {
        if (liveStreamService is null || livePlayerService is null)
        {
            return;
        }

        try
        {
            var account = resolveAccount?.Invoke(this);
            var source = await RefreshLiveSourceAsync(true);
            var oldToken = livePlayerToken;
            var session = await livePlayerService.CreatePlayerAsync(source, account?.Cookie);

            livePlayerService.Revoke(oldToken);
            livePlayerToken = session.Token;
            LivePlayerUrl = session.PlayerUrl;
            IsLivePlayerVisible = true;
            StartListeningWithLivePlayerIfNeeded();
        }
        catch
        {
            if (string.IsNullOrWhiteSpace(livePlayerToken))
            {
                IsLivePlayerVisible = false;
            }
        }
    }

    private void StartListeningWithLivePlayerIfNeeded()
    {
        if (IsListening || getSettings?.Invoke().AutoStartListeningWithLivePlayer != true)
        {
            return;
        }

        StartListening();
    }

    private static double NormalizeLivePlayerVolumePercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return MAX_LIVE_PLAYER_VOLUME_PERCENT;
        }

        return Math.Clamp(value, MIN_LIVE_PLAYER_VOLUME_PERCENT, MAX_LIVE_PLAYER_VOLUME_PERCENT);
    }

    private static bool ShouldNormalizeLivePlayerVolumePercent(double value, double normalizedValue)
    {
        return double.IsNaN(value) ||
               double.IsInfinity(value) ||
               Math.Abs(normalizedValue - value) > double.Epsilon;
    }

    private async Task<LiveStreamPlaySource> RefreshLiveSourceAsync(bool throwOnError)
    {
        if (liveStreamService is null)
        {
            throw new InvalidOperationException("直播流服务未初始化");
        }

        try
        {
            var account = resolveAccount?.Invoke(this);
            var settings = getSettings?.Invoke();
            var source = await liveStreamService.GetPlayableSourceAsync(
                RoomId,
                SelectedLiveQuality?.Quality ?? 0,
                account?.Cookie,
                settings is null ? null : TimeSpan.FromSeconds(settings.TimeoutSeconds));

            RefreshLiveQualityOptions(source.QualityOptions, source.CurrentQuality);

            return source;
        }
        catch when (!throwOnError)
        {
            return new();
        }
    }

    private void RefreshLiveQualityOptions(IEnumerable<LiveQualityOption> qualityOptions, int currentQuality)
    {
        suppressLiveQualityChange = true;
        LiveQualityOptions.Clear();

        foreach (var option in qualityOptions)
        {
            LiveQualityOptions.Add(option);
        }

        SelectedLiveQuality = LiveQualityOptions.FirstOrDefault(option => option.Quality == currentQuality) ??
                              LiveQualityOptions.FirstOrDefault();
        suppressLiveQualityChange = false;
    }

    /// <summary>
    /// 加载歌词库条目。
    /// </summary>
    public void LoadLyric(LyricLibraryItem item)
    {
        LyricTitle = item.Title;
        LyricInput = item.LyricText;
        TranslatedLyricInput = item.TranslatedLyricText;
        ApplyLyric();
    }

    /// <summary>
    /// 计算歌词自动发送等待时间。
    /// </summary>
    public static TimeSpan CalculateLyricPlaybackWait(TimeSpan due, TimeSpan elapsed, double playbackRate)
    {
        var normalizedRate = NormalizeLyricPlaybackRate(playbackRate);
        var scaledDue = TimeSpan.FromTicks((long)Math.Round(due.Ticks / normalizedRate));
        var wait = scaledDue - elapsed;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    private static double NormalizeLyricPlaybackRate(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return DEFAULT_LYRIC_PLAYBACK_RATE;
        }

        return Math.Clamp(value, MIN_LYRIC_PLAYBACK_RATE, MAX_LYRIC_PLAYBACK_RATE);
    }

    private static LyricSendMode NormalizeLyricSendMode(LyricSendMode value)
    {
        return Enum.IsDefined(value) ? value : LyricSendMode.Bilingual;
    }

    private DanmuForwardRuleViewModel CreateForwardRuleViewModel(DanmuForwardRuleState state)
    {
        var rule = new DanmuForwardRuleViewModel(state, OnForwardRuleChanged);
        ConfigureForwardRule(rule);
        return rule;
    }

    private void ConfigureForwardRule(DanmuForwardRuleViewModel rule)
    {
        rule.ConfigureOptions(ForwardSourceRooms, forwardAccounts, getSymbolGroups?.Invoke() ?? []);

        if (userNameService is not null)
        {
            rule.ConfigureUserNameLookup(userNameService, () => getSettings is null
                ? null
                : TimeSpan.FromSeconds(getSettings().TimeoutSeconds));
        }
    }

    private void OnForwardRuleChanged(DanmuForwardRuleViewModel rule)
    {
        _ = HandleForwardRuleChangedAsync(rule);
    }

    private async Task HandleForwardRuleChangedAsync(DanmuForwardRuleViewModel rule)
    {
        StartForwardRule(rule);
        await SaveStateAsync();
    }

    /// <summary>
    /// 重启已启用的转发规则。
    /// </summary>
    public void RestartForwarding()
    {
        foreach (var rule in ForwardRules.Where(rule => rule.IsEnabled))
        {
            StartForwardRule(rule);
        }
    }

    private void StartForwardRule(DanmuForwardRuleViewModel rule)
    {
        StopForwardRule(rule);

        if (!rule.IsEnabled)
        {
            SetForwardRuleStatus(rule, "未启用");
            return;
        }

        RefreshForwardSourceRooms();
        var source = ForwardSourceRooms.FirstOrDefault(item => item.Key == rule.SourceRoomKey);

        if (source is null)
        {
            SetForwardRuleStatus(rule, "请选择源直播间");
            return;
        }

        var error = forwardService.Validate(rule.State);

        if (!string.IsNullOrWhiteSpace(error))
        {
            SetForwardRuleStatus(rule, $"正则错误：{error}");
            return;
        }

        var sourceAccount = resolveAccountByRoom?.Invoke(source.WorkspaceId, source.RoomStateId);
        var targetAccount = ResolveForwardTargetAccount(rule, resolveAccount?.Invoke(this), out var targetAccountError);
        var settings = getSettings?.Invoke();

        if (sourceAccount is null || string.IsNullOrWhiteSpace(sourceAccount.Cookie))
        {
            SetForwardRuleStatus(rule, "源直播间缺少账号");
            return;
        }

        if (settings is null)
        {
            SetForwardRuleStatus(rule, "缺少发送设置");
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetAccountError) || targetAccount is null)
        {
            SetForwardRuleStatus(rule, targetAccountError);
            return;
        }

        if (!long.TryParse(source.RoomId, out var sourceRoomId))
        {
            SetForwardRuleStatus(rule, "源房间号格式错误");
            return;
        }

        var tokenSource = new CancellationTokenSource();
        forwardTokenSources[rule.Id] = tokenSource;
        SetForwardRuleStatus(rule, "监听中");

        _ = Task.Run(async () =>
        {
            var socket = liveDanmuSocketFactory.Create(sourceRoomId, sourceAccount.Cookie);
            socket.DanmuReceived += (_, message) =>
                _ = ForwardDanmuAsync(rule, message, targetAccount, settings, tokenSource.Token);
            socket.ErrorReceived += (_, _) => SetForwardRuleStatus(rule, "连接中断，正在重连");
            socket.Disconnected += (_, _) => SetForwardRuleStatus(rule, "连接中断，正在重连");
            socket.Recovered += (_, _) => SetForwardRuleStatus(rule, "监听中");

            try
            {
                await socket.StartAsync(tokenSource.Token);
            }
            catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
            {
            }
            finally
            {
                if (forwardTokenSources.TryGetValue(rule.Id, out var currentTokenSource) &&
                    currentTokenSource == tokenSource)
                {
                    forwardTokenSources.Remove(rule.Id);
                }

                if (!tokenSource.IsCancellationRequested && rule.IsEnabled)
                {
                    SetForwardRuleStatus(rule, "已停止");
                }

                if (socket is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                tokenSource.Dispose();
            }
        });
    }

    private BilibiliAccount? ResolveForwardTargetAccount(
        DanmuForwardRuleViewModel rule,
        BilibiliAccount? fallbackAccount,
        out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(rule.AccountOverrideId))
        {
            if (fallbackAccount is null || string.IsNullOrWhiteSpace(fallbackAccount.Cookie))
            {
                error = "目标直播间缺少账号";
                return null;
            }

            return fallbackAccount;
        }

        var account = resolveAccountById?.Invoke(rule.AccountOverrideId);

        if (account is null)
        {
            error = "指定账号不存在";
            return null;
        }

        if (string.IsNullOrWhiteSpace(account.Cookie))
        {
            error = "指定账号缺少 Cookie";
            return null;
        }

        return account;
    }

    private async Task ForwardDanmuAsync(
        DanmuForwardRuleViewModel rule,
        BilibiliDanmuMessage message,
        BilibiliAccount targetAccount,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (!forwardService.IsMatch(message, rule.State))
            {
                return;
            }

            var markGroup = getMarkSymbolGroup?.Invoke() ?? MarkSymbolService.CreateDefaultGroup(settings);
            var messageText = forwardService.CreateForwardMessage(
                message,
                rule.State,
                markGroup,
                getSymbolGroups?.Invoke() ?? [markGroup]);
            await SendMessageAsync(messageText, targetAccount, settings, cancellationToken);
            SetForwardRuleStatus(rule, $"已转发 {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception exception)
        {
            SetForwardRuleStatus(rule, $"转发失败：{exception.Message}");
        }
    }

    private void StopForwardRule(DanmuForwardRuleViewModel rule)
    {
        if (forwardTokenSources.Remove(rule.Id, out var tokenSource))
        {
            tokenSource.Cancel();
        }

        SetForwardRuleStatus(rule, rule.IsEnabled ? "已停止" : "未启用");
    }

    private void SetForwardRuleStatus(DanmuForwardRuleViewModel rule, string status)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            rule.StatusText = status;
            return;
        }

        Dispatcher.UIThread.Post(() => rule.StatusText = status);
    }

    private Task SaveStateAsync()
    {
        return saveState?.Invoke() ?? Task.CompletedTask;
    }

    private async Task<List<DanmuSendResult>> SendMessageAsync(
        string message,
        BilibiliAccount? account,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (sendService is null)
        {
            return [];
        }

        var results = new List<DanmuSendResult>();
        var roomId = RoomId;
        var sendingAccount = await PrepareDanmuSendingAccountAsync(roomId, account, cancellationToken);

        foreach (var part in sendService.SplitMessage(message, 30))
        {
            results.Add(await sendService.SendAsync(roomId, part, sendingAccount, settings, cancellationToken));
        }

        return results;
    }

    private bool TrySetDraftSendFeedback(long operationId, long revision, string status, bool failed)
    {
        if (operationId != draftSendOperationId || revision != draftRevision)
        {
            return false;
        }

        DraftSendStatus = status;
        IsDraftSendFailed = failed;
        return true;
    }

    private void InvalidateDraftSendFeedback()
    {
        draftRevision++;
        IsDraftSendFailed = false;
    }

    private async Task RunAutoLyricAsync(CancellationTokenSource tokenSource)
    {
        var cancellationToken = tokenSource.Token;

        try
        {
            ResetLyricPlaybackClock(LyricPlaybackPositionSeconds);

            while (!cancellationToken.IsCancellationRequested)
            {
                var elapsed = DateTimeOffset.Now - lyricPlaybackStartedAt;
                var nextPosition = lyricPlaybackStartPositionSeconds +
                                   (elapsed.TotalSeconds * NormalizeLyricPlaybackRate(LyricPlaybackRate));

                if (nextPosition >= GetLastLyricEndPosition())
                {
                    break;
                }

                var targetIndex = ActiveLyricLine is { TimeSeconds: < 0 } ? ActiveLyricIndex : (int?)null;
                SetLyricPlaybackPosition(nextPosition, targetIndex);
                await SendActiveLyricIfNeededAsync(cancellationToken);
                await Task.Delay(LYRIC_PLAYBACK_TICK_MS, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(lyricTokenSource, tokenSource))
            {
                lyricTokenSource = null;
                IsLyricAutoSending = false;
            }

            tokenSource.Dispose();
        }
    }

    private void RefreshActiveLyric()
    {
        for (var index = 0; index < Lyrics.Count; index++)
        {
            Lyrics[index].IsActive = index == ActiveLyricIndex;
        }

        ActiveLyricLine = ActiveLyricIndex >= 0 && ActiveLyricIndex < Lyrics.Count
            ? Lyrics[ActiveLyricIndex]
            : null;
        SeekNextLyricCommand.NotifyCanExecuteChanged();
    }

    private void OnDanmuReceived(object? sender, BilibiliDanmuMessage message)
    {
        if (!ReferenceEquals(sender, activeListenSocket))
        {
            return;
        }

        if (ShouldHideDanmu(message))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, activeListenSocket))
            {
                AddDanmuToFeed(message);
            }
        });
    }

    internal void AddDanmu(BilibiliDanmuMessage message)
    {
        if (!ShouldHideDanmu(message))
        {
            AddDanmuToFeed(message);
        }
    }

    private bool ShouldHideDanmu(BilibiliDanmuMessage message)
    {
        return getSettings?.Invoke().HideEmoticonDanmu == true && message.IsEmoticon;
    }

    private void AddDanmuToFeed(BilibiliDanmuMessage message)
    {
        var item = new DanmuFeedItem
        {
            RoomId = message.RoomId,
            IsRoomAdmin = message.IsRoomAdmin,
            Time = DateTimeOffset.Now,
            UserName = message.UserName,
            UserUid = message.Uid.ToString(),
            MedalName = message.MedalName,
            MedalLevel = message.MedalLevel,
            MedalGuardLevel = message.MedalGuardLevel,
            Content = message.Content
        };
        DanmuItems.Insert(0, item);
        TrimDanmuItems();

        if (emoticonCacheService is not null && message.Emoticons.Count > 0)
        {
            _ = LoadDanmuEmoticonsAsync(item, message.Emoticons, emoticonCacheService,
                listenTokenSource?.Token ?? CancellationToken.None);
        }
    }

    private async Task LoadDanmuEmoticonsAsync(
        DanmuFeedItem item,
        IReadOnlyList<BilibiliDanmuEmoticon> emoticons,
        IEmoticonCacheService cacheService,
        CancellationToken cancellationToken)
    {
        try
        {
            var downloads = emoticons.Select(async emoticon => new DanmuEmoticon
            {
                Text = emoticon.Text,
                ImagePath = await cacheService.GetEmoticonAsync(emoticon.Url, cancellationToken).ConfigureAwait(false)
            });
            var cachedEmoticons = await Task.WhenAll(downloads).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested && DanmuItems.Contains(item))
                {
                    item.Emoticons = cachedEmoticons.Where(emoticon => !string.IsNullOrWhiteSpace(emoticon.ImagePath))
                        .ToArray();
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceWarning($"表情图片加载失败：{exception.GetType().Name}");
        }
    }

    private void OnGiftReceived(object? sender, BilibiliGiftMessage message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, activeListenSocket))
            {
                AddGiftToFeed(message);
            }
        });
    }

    internal void AddGift(BilibiliGiftMessage message)
    {
        AddGiftToFeed(message);
    }

    private void AddGiftToFeed(BilibiliGiftMessage message)
    {
        if (message.Uid <= 0 || string.IsNullOrWhiteSpace(message.UserName) ||
            string.IsNullOrWhiteSpace(message.GiftName) || message.Quantity <= 0)
        {
            return;
        }

        var comboIdentity = CreateGiftComboIdentity(message);

        if (!string.IsNullOrWhiteSpace(comboIdentity) && giftComboItems.TryGetValue(comboIdentity, out var combo))
        {
            if (DanmuItems.Contains(combo.Item))
            {
                var count = message.BatchComboCount > 0
                    ? Math.Max(combo.Count, message.BatchComboCount)
                    : (int)Math.Min(int.MaxValue, (long)combo.Count + message.Quantity);
                var unitPrice = message.UnitPrice > 0 ? message.UnitPrice : combo.UnitPrice;
                combo.Item.Content = CreateGiftContent(message.GiftName, count, unitPrice);
                giftComboItems[comboIdentity] = (combo.Item, count, unitPrice, combo.OrderNode);
                return;
            }

            RemoveGiftCombo(combo.Item);
        }

        var initialCount = Math.Max(message.Quantity, message.BatchComboCount);
        var item = new DanmuFeedItem
        {
            RoomId = message.RoomId,
            Time = message.Timestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp)
                : DateTimeOffset.Now,
            UserName = message.UserName,
            UserUid = message.Uid.ToString(CultureInfo.InvariantCulture),
            Content = CreateGiftContent(message.GiftName, initialCount, message.UnitPrice)
        };

        DanmuItems.Insert(0, item);
        TrimDanmuItems();

        if (!string.IsNullOrWhiteSpace(comboIdentity))
        {
            AddGiftCombo(comboIdentity, item, initialCount, message.UnitPrice);
        }
    }

    private static string CreateGiftComboIdentity(BilibiliGiftMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.BatchComboId))
        {
            return "";
        }

        var giftIdentity = string.IsNullOrWhiteSpace(message.GiftId) ? message.GiftName : message.GiftId;
        return string.Join(
            '|',
            message.RoomId,
            message.Uid.ToString(CultureInfo.InvariantCulture),
            giftIdentity,
            message.BatchComboId);
    }

    private static string CreateGiftContent(string giftName, int count, decimal unitPrice)
    {
        var content = $"赠送 {giftName} × {count}";
        var totalPrice = unitPrice * count / 1000m;

        if (totalPrice > 0)
        {
            content += $" · ￥{totalPrice.ToString("0.##", CultureInfo.InvariantCulture)}";
        }

        return content;
    }

    private void AddGiftCombo(string identity, DanmuFeedItem item, int count, decimal unitPrice)
    {
        while (giftComboItems.Count >= MAX_RECENT_GIFT_COMBOS && giftComboOrder.First is { } oldest)
        {
            if (giftComboItems.TryGetValue(oldest.Value, out var oldestCombo))
            {
                RemoveGiftCombo(oldestCombo.Item);
            }
            else
            {
                giftComboOrder.RemoveFirst();
            }
        }

        var orderNode = giftComboOrder.AddLast(identity);
        giftComboItems[identity] = (item, count, unitPrice, orderNode);
        giftComboKeysByItem[item] = identity;
    }

    private void RemoveGiftCombo(DanmuFeedItem item)
    {
        if (!giftComboKeysByItem.Remove(item, out var identity) || !giftComboItems.Remove(identity, out var combo))
        {
            return;
        }

        giftComboOrder.Remove(combo.OrderNode);
    }

    private void ClearGiftComboState()
    {
        giftComboItems.Clear();
        giftComboKeysByItem.Clear();
        giftComboOrder.Clear();
    }

    private void TrimDanmuItems()
    {
        while (DanmuItems.Count > 300)
        {
            var removedItem = DanmuItems[^1];
            DanmuItems.RemoveAt(DanmuItems.Count - 1);
            RemoveGiftCombo(removedItem);
        }
    }

    private void OnSuperChatReceived(object? sender, BilibiliSuperChatMessage message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, activeListenSocket))
            {
                AddSuperChat(message);
            }
        });
    }

    internal void AddSuperChat(BilibiliSuperChatMessage message)
    {
        var identity = CreateSuperChatIdentity(message);

        if (!recentSuperChatIdentities.Add(identity))
        {
            return;
        }

        recentSuperChatIdentityOrder.Enqueue(identity);

        while (recentSuperChatIdentityOrder.Count > MAX_RECENT_SUPER_CHAT_IDENTITIES)
        {
            recentSuperChatIdentities.Remove(recentSuperChatIdentityOrder.Dequeue());
        }

        SuperChats.Insert(0, new()
        {
            MessageId = message.MessageId,
            Time = DateTimeOffset.FromUnixTimeSeconds(message.Timestamp),
            UserName = message.UserName,
            PriceText = message.PriceText,
            BorderColor = CreateSuperChatBorderColor(message.BorderColor),
            Content = message.Content
        });
        Trim(SuperChats, 100);
    }

    private static string CreateSuperChatIdentity(BilibiliSuperChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.MessageId))
        {
            return $"id:{message.MessageId.Trim()}";
        }

        return string.Join(
            '|',
            "fallback",
            message.RoomId,
            message.Timestamp.ToString(CultureInfo.InvariantCulture),
            message.UserName,
            message.Price.ToString(CultureInfo.InvariantCulture),
            message.Content);
    }

    private void OnGuardPurchaseReceived(object? sender, BilibiliGuardPurchaseMessage message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, activeListenSocket))
            {
                AddGuardPurchase(message);
            }
        });
    }

    internal void AddGuardPurchase(BilibiliGuardPurchaseMessage message)
    {
        if (message.GuardLevel is not (1 or 2 or 3) || message.Uid <= 0)
        {
            return;
        }

        var identity = message.Identity;

        if (!string.IsNullOrWhiteSpace(identity))
        {
            if (!recentGuardPurchaseIdentities.Add(identity))
            {
                return;
            }

            recentGuardPurchaseIdentityOrder.Enqueue(identity);

            while (recentGuardPurchaseIdentityOrder.Count > MAX_RECENT_SUPER_CHAT_IDENTITIES)
            {
                recentGuardPurchaseIdentities.Remove(recentGuardPurchaseIdentityOrder.Dequeue());
            }
        }

        SuperChats.Insert(0, new()
        {
            Kind = LivePaidEventKind.GuardPurchase,
            MessageId = message.MessageId,
            Time = message.Timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp) : DateTimeOffset.Now,
            UserName = message.UserName,
            GuardLevelName = message.GuardLevelName,
            BorderColor = GuardPurchaseStyleService.GetBorderColor(message.GuardLevel)
        });
        Trim(SuperChats, 100);
    }

    private static Color CreateSuperChatBorderColor(string colorText)
    {
        return Color.TryParse(colorText, out var color) ? color : Color.Parse(DEFAULT_SUPER_CHAT_COLOR);
    }

    private void OnSendRecordCreated(object? sender, DanmuFeedItem item)
    {
        if (item.UserName != RoomId)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            TranslateHistoryItems.Insert(0, item);
            Trim(TranslateHistoryItems, 1000);
            OnPropertyChanged(nameof(HasTranslateHistory));

            if (item.IsSendAccepted)
            {
                return;
            }

            DanmuItems.Insert(0, item);
            TrimDanmuItems();
        });
    }

    private static void Trim<T>(ObservableCollection<T> collection, int maxCount)
    {
        while (collection.Count > maxCount)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }
}
