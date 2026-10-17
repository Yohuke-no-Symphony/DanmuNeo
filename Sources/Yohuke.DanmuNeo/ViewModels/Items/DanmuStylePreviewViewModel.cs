using System.Collections.ObjectModel;
using Avalonia.Media;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

internal sealed class DanmuStylePreviewViewModel : ViewModelBase, IDisposable
{
    private const int DANMU_BATCH_SIZE = 80;

    private string userName = Localization.Get("Preview.DefaultUserName");
    private readonly List<Action> sampleLocalizers = [];
    private bool isUserNameEdited;
    private bool isContentEdited;
    private string content = Localization.Get("Preview.DefaultContent");
    private string danmuStatus = "";
    private string medalName = "肉肉";
    private int medalLevel = 25;
    private string superChatColorText = "";
    private bool isCompactDanmuDisplay;
    private bool isCompactDanmuWrap;
    private bool isCompactSuperChatWrap = true;
    private SuperChatStylePreset? selectedSuperChatPreset;
    private GuardStylePreset? selectedGuardPreset;
    private GuardStylePreset? selectedMedalGuardPreset;

    /// <summary>
    /// 创建礼物、六档 SC、三档上舰和不同粉丝牌场景的离线默认样本。
    /// </summary>
    public DanmuStylePreviewViewModel()
    {
        SuperChatPresets =
        [
            CreatePreset(30, 300, "Preview.Color.Blue", "#3171D2"),
            CreatePreset(50, 500, "Preview.Color.Cyan", "#427D9E"),
            CreatePreset(100, 1000, "Preview.Color.Gold", "#E2B52B"),
            CreatePreset(500, 5000, "Preview.Color.Orange", "#E09443"),
            CreatePreset(1000, 10000, "Preview.Color.Red", "#E54D4D"),
            CreatePreset(2000, 20000, "Preview.Color.DarkRed", "#AB1A32")
        ];
        GuardPresets =
        [
            new()
            {
                GuardLevel = 1
            },
            new()
            {
                GuardLevel = 2
            },
            new()
            {
                GuardLevel = 3
            }
        ];
        MedalGuardPresets = [new() { GuardLevel = 0 }, .. GuardPresets];
        SelectedSuperChatPreset = SuperChatPresets[0];
        SelectedGuardPreset = GuardPresets[^1];
        SelectedMedalGuardPreset = MedalGuardPresets[0];
        ResetSamples();
        Localization.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<SuperChatItem> SuperChats { get; } = [];

    public ObservableCollection<DanmuFeedItem> DanmuItems { get; } = [];

    public IReadOnlyList<SuperChatStylePreset> SuperChatPresets { get; }

    /// <summary>
    /// 可在离线预览中选择的大航海档位。
    /// </summary>
    public IReadOnlyList<GuardStylePreset> GuardPresets { get; }

    /// <summary>
    /// 普通粉丝牌和三个大航海档位的预览选项。
    /// </summary>
    public IReadOnlyList<GuardStylePreset> MedalGuardPresets { get; }

    /// <summary>
    /// 新增弹幕的粉丝牌大航海身份。
    /// </summary>
    public GuardStylePreset? SelectedMedalGuardPreset
    {
        get => selectedMedalGuardPreset;
        set => SetProperty(ref selectedMedalGuardPreset, value);
    }

    public bool IsCompactDanmuDisplay
    {
        get => isCompactDanmuDisplay;
        set => SetProperty(ref isCompactDanmuDisplay, value);
    }

    /// <summary>
    /// 紧凑预览中是否允许普通弹幕自动换行。
    /// </summary>
    public bool IsCompactDanmuWrap
    {
        get => isCompactDanmuWrap;
        set => SetProperty(ref isCompactDanmuWrap, value);
    }

    /// <summary>
    /// 紧凑预览中是否允许醒目留言自动换行。
    /// </summary>
    public bool IsCompactSuperChatWrap
    {
        get => isCompactSuperChatWrap;
        set => SetProperty(ref isCompactSuperChatWrap, value);
    }

    public string UserName
    {
        get => userName;
        set
        {
            if (SetProperty(ref userName, value))
            {
                isUserNameEdited = true;
            }
        }
    }

    public string Content
    {
        get => content;
        set
        {
            if (SetProperty(ref content, value))
            {
                isContentEdited = true;
            }
        }
    }

    public string DanmuStatus
    {
        get => danmuStatus;
        set => SetProperty(ref danmuStatus, value);
    }

    /// <summary>
    /// 新增普通弹幕的粉丝牌名称；留空时预览无牌用户。
    /// </summary>
    public string MedalName
    {
        get => medalName;
        set => SetProperty(ref medalName, value);
    }

    /// <summary>
    /// 新增普通弹幕的粉丝牌等级；零级不显示徽章。
    /// </summary>
    public int MedalLevel
    {
        get => medalLevel;
        set => SetProperty(ref medalLevel, Math.Max(0, value));
    }

    public string SuperChatColorText
    {
        get => superChatColorText;
        set => SetProperty(ref superChatColorText, value);
    }

    public SuperChatStylePreset? SelectedSuperChatPreset
    {
        get => selectedSuperChatPreset;
        set
        {
            if (!SetProperty(ref selectedSuperChatPreset, value) || value is null)
            {
                return;
            }

            SuperChatColorText = value.BorderColor.ToString();
        }
    }

    /// <summary>
    /// 下一条上舰记录使用的档位。
    /// </summary>
    public GuardStylePreset? SelectedGuardPreset
    {
        get => selectedGuardPreset;
        set => SetProperty(ref selectedGuardPreset, value);
    }

    internal void AddDanmu()
    {
        DanmuItems.Insert(0, CreateDanmuItem(DateTimeOffset.Now, NormalizeContent(Content)));
    }

    internal void AddDanmuBatch()
    {
        var now = DateTimeOffset.Now;
        var contentText = NormalizeContent(Content);

        for (var index = 0; index < DANMU_BATCH_SIZE; index++)
        {
            DanmuItems.Insert(0, CreateDanmuItem(
                now.AddMilliseconds(index),
                Localization.Format("Preview.BatchContent", contentText, index + 1, DANMU_BATCH_SIZE)));
        }
    }

    internal void AddSuperChat()
    {
        if (SelectedSuperChatPreset is not { } preset)
        {
            return;
        }

        var borderColor = Color.TryParse(SuperChatColorText, out var customColor)
            ? customColor
            : preset.BorderColor;
        SuperChats.Insert(0, CreateSuperChatItem(
            preset,
            NormalizeUserName(UserName),
            NormalizeContent(Content),
            DateTimeOffset.Now,
            borderColor));
    }

    internal void AddGuardPurchase()
    {
        if (SelectedGuardPreset is not { } preset)
        {
            return;
        }

        SuperChats.Insert(0, CreateGuardPurchaseItem(preset, NormalizeUserName(UserName), DateTimeOffset.Now));
    }

    internal void ClearSamples()
    {
        sampleLocalizers.Clear();
        SuperChats.Clear();
        DanmuItems.Clear();
    }

    internal void ResetSamples()
    {
        ClearSamples();
        var now = DateTimeOffset.Now;

        for (var index = 0; index < SuperChatPresets.Count; index++)
        {
            var preset = SuperChatPresets[index];
            var item = CreateSuperChatItem(preset, "", "", now.AddMinutes(-index), preset.BorderColor);
            SuperChats.Add(item);
            RegisterSampleLocalization(() =>
            {
                item.UserName = Localization.Format("Preview.SuperChatUser", preset.ColorName);
                item.Content = Localization.Format("Preview.SuperChatContent", preset.PriceText);
            });
        }

        for (var index = 0; index < GuardPresets.Count; index++)
        {
            var preset = GuardPresets[index];
            var item = CreateGuardPurchaseItem(preset, "", now.AddMinutes(-SuperChatPresets.Count - index));
            SuperChats.Add(item);
            RegisterSampleLocalization(() => item.UserName = Localization.Format("Preview.GuardUser", preset.DisplayName));
        }

        AddDanmuSample(new() { Time = now, UserUid = "10001" }, "Preview.ShortUser", "Preview.ShortContent");
        AddDanmuSample(new() { Time = now.AddSeconds(-4), UserUid = "10005" }, "Preview.GiftUser", "Preview.GiftContent");
        AddDanmuSample(new()
        {
            Time = now.AddSeconds(-8),
            UserUid = "10002",
            MedalName = "肉肉",
            MedalLevel = 25
        }, "Preview.LongUser", "Preview.LongContent");
        AddDanmuSample(new()
        {
            Time = now.AddSeconds(-16),
            UserUid = "10003",
            MedalName = "牵丝蝠",
            MedalLevel = 8
        }, "Preview.EmojiUser", "Preview.MixedContent");
        var statusItem = new DanmuFeedItem { Time = now.AddSeconds(-24), UserUid = "10004" };
        statusItem.SetLocalizedStatus("Preview.Forwarded");
        AddDanmuSample(statusItem, "Preview.StatusUser", "Preview.StatusContent");

        foreach (var preset in GuardPresets)
        {
            var item = new DanmuFeedItem
            {
                Time = now.AddSeconds(-32 - preset.GuardLevel),
                UserUid = $"medal-guard-{preset.GuardLevel}",
                MedalName = "肉肉",
                MedalLevel = 25,
                MedalGuardLevel = preset.GuardLevel
            };
            DanmuItems.Add(item);
            RegisterSampleLocalization(() =>
            {
                item.UserName = Localization.Format("Preview.MedalUser", preset.DisplayName);
                item.Content = Localization.Format("Preview.MedalContent", preset.DisplayName);
            });
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Localization.LanguageChanged -= OnLanguageChanged;
        sampleLocalizers.Clear();
        GC.SuppressFinalize(this);
    }

    private void AddDanmuSample(DanmuFeedItem item, string userNameKey, string contentKey)
    {
        DanmuItems.Add(item);
        RegisterSampleLocalization(() =>
        {
            item.UserName = Localization.Get(userNameKey);
            item.SetLocalizedContent(contentKey);
        });
    }

    private void RegisterSampleLocalization(Action refresh)
    {
        sampleLocalizers.Add(refresh);
        refresh();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (!isUserNameEdited)
        {
            SetProperty(ref userName, Localization.Get("Preview.DefaultUserName"), nameof(UserName));
        }

        if (!isContentEdited)
        {
            SetProperty(ref content, Localization.Get("Preview.DefaultContent"), nameof(Content));
        }

        foreach (var preset in SuperChatPresets)
        {
            preset.RefreshLocalization();
        }

        foreach (var preset in MedalGuardPresets)
        {
            preset.RefreshLocalization();
        }

        foreach (var refresh in sampleLocalizers)
        {
            refresh();
        }

        foreach (var item in DanmuItems)
        {
            item.RefreshLocalization();
        }

        foreach (var item in SuperChats)
        {
            item.RefreshLocalization();
        }
    }

    private DanmuFeedItem CreateDanmuItem(DateTimeOffset time, string contentText)
    {
        return new()
        {
            Time = time,
            UserName = NormalizeUserName(UserName),
            UserUid = "debug-user",
            Content = contentText,
            Status = DanmuStatus.Trim(),
            MedalName = MedalName.Trim(),
            MedalLevel = MedalLevel,
            MedalGuardLevel = SelectedMedalGuardPreset?.GuardLevel ?? 0
        };
    }

    private static SuperChatStylePreset CreatePreset(
        decimal cnyPrice,
        int batteryAmount,
        string colorName,
        string colorText)
    {
        return new()
        {
            CnyPrice = cnyPrice,
            BatteryAmount = batteryAmount,
            ColorNameKey = colorName,
            BorderColor = Color.Parse(colorText)
        };
    }

    private static SuperChatItem CreateSuperChatItem(
        SuperChatStylePreset preset,
        string userName,
        string content,
        DateTimeOffset time,
        Color borderColor)
    {
        return new()
        {
            Kind = LivePaidEventKind.SuperChat,
            MessageId = $"debug-{preset.BatteryAmount}-{time.ToUnixTimeMilliseconds()}",
            Time = time,
            UserName = userName,
            BatteryAmount = preset.BatteryAmount,
            BorderColor = borderColor,
            Content = content
        };
    }

    private static SuperChatItem CreateGuardPurchaseItem(
        GuardStylePreset preset,
        string userName,
        DateTimeOffset time)
    {
        return new()
        {
            Kind = LivePaidEventKind.GuardPurchase,
            MessageId = $"debug-guard-{preset.GuardLevel}-{Guid.NewGuid():N}",
            Time = time,
            UserName = userName,
            GuardLevel = preset.GuardLevel,
            BorderColor = GuardPurchaseStyleService.GetBorderColor(preset.GuardLevel)
        };
    }

    private static string NormalizeUserName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? Localization.Get("Preview.FallbackUserName") : value.Trim();
    }

    private static string NormalizeContent(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? Localization.Get("Preview.FallbackContent") : value.Trim();
    }
}
