using System.Collections.ObjectModel;
using Avalonia.Media;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

internal sealed class DanmuStylePreviewViewModel : ViewModelBase
{
    private const int DANMU_BATCH_SIZE = 80;

    private string userName = "样式测试用户";
    private string content = "这是一条用于检查字号、间距和自动换行的测试消息。";
    private string danmuStatus = "";
    private string medalName = "星光";
    private int medalLevel = 25;
    private string superChatColorText = "";
    private bool isCompactDanmuDisplay;
    private SuperChatStylePreset? selectedSuperChatPreset;
    private GuardStylePreset? selectedGuardPreset;
    private GuardStylePreset? selectedMedalGuardPreset;

    /// <summary>
    /// 创建六档 SC、三档上舰和不同粉丝牌场景的离线默认样本。
    /// </summary>
    public DanmuStylePreviewViewModel()
    {
        SuperChatPresets =
        [
            CreatePreset(30, 300, "蓝色", "#3171D2"),
            CreatePreset(50, 500, "青蓝", "#427D9E"),
            CreatePreset(100, 1000, "金色", "#E2B52B"),
            CreatePreset(500, 5000, "橙色", "#E09443"),
            CreatePreset(1000, 10000, "红色", "#E54D4D"),
            CreatePreset(2000, 20000, "深红", "#AB1A32")
        ];
        GuardPresets =
        [
            new()
            {
                GuardLevel = 1,
                DisplayName = "总督"
            },
            new()
            {
                GuardLevel = 2,
                DisplayName = "提督"
            },
            new()
            {
                GuardLevel = 3,
                DisplayName = "舰长"
            }
        ];
        MedalGuardPresets = [new() { GuardLevel = 0, DisplayName = "普通" }, .. GuardPresets];
        SelectedSuperChatPreset = SuperChatPresets[0];
        SelectedGuardPreset = GuardPresets[^1];
        SelectedMedalGuardPreset = MedalGuardPresets[0];
        ResetSamples();
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

    public string UserName
    {
        get => userName;
        set => SetProperty(ref userName, value);
    }

    public string Content
    {
        get => content;
        set => SetProperty(ref content, value);
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
                $"{contentText} （批量 {index + 1}/{DANMU_BATCH_SIZE}）"));
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
            SuperChats.Add(CreateSuperChatItem(
                preset,
                $"{preset.ColorName}档用户",
                $"这是 {preset.PriceText} 档醒目留言，用于检查边框、金额和长文本换行效果。",
                now.AddMinutes(-index),
                preset.BorderColor));
        }

        for (var index = 0; index < GuardPresets.Count; index++)
        {
            var preset = GuardPresets[index];
            SuperChats.Add(CreateGuardPurchaseItem(
                preset,
                $"{preset.DisplayName}上舰用户",
                now.AddMinutes(-SuperChatPresets.Count - index)));
        }

        DanmuItems.Add(new()
        {
            Time = now,
            UserName = "短名",
            UserUid = "10001",
            Content = "普通短弹幕"
        });
        DanmuItems.Add(new()
        {
            Time = now.AddSeconds(-8),
            UserName = "这是一个很长很长的用户名",
            UserUid = "10002",
            MedalName = "星光",
            MedalLevel = 25,
            Content = "这是一条很长的弹幕，用于检查预览区域较窄时用户名裁切以及正文自动换行是否清晰自然。"
        });
        DanmuItems.Add(new()
        {
            Time = now.AddSeconds(-16),
            UserName = "Emoji 用户 🎉",
            UserUid = "10003",
            MedalName = "吸夕猫",
            MedalLevel = 8,
            Content = "中日文、English、数字 12345 与 Emoji ✨🚀 混排测试。"
        });
        DanmuItems.Add(new()
        {
            Time = now.AddSeconds(-24),
            UserName = "状态测试",
            UserUid = "10004",
            Content = "带有状态文本的弹幕。",
            Status = "已转发"
        });

        foreach (var preset in GuardPresets)
        {
            DanmuItems.Add(new()
            {
                Time = now.AddSeconds(-32 - preset.GuardLevel),
                UserName = $"{preset.DisplayName}粉丝牌用户",
                UserUid = $"medal-guard-{preset.GuardLevel}",
                MedalName = "星光",
                MedalLevel = 25,
                MedalGuardLevel = preset.GuardLevel,
                Content = $"{preset.DisplayName}粉丝牌边缘色测试。"
            });
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
            ColorName = colorName,
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
            PriceText = preset.PriceText,
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
            GuardLevelName = preset.DisplayName,
            BorderColor = GuardPurchaseStyleService.GetBorderColor(preset.GuardLevel)
        };
    }

    private static string NormalizeUserName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "测试用户" : value.Trim();
    }

    private static string NormalizeContent(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "空内容样式测试" : value.Trim();
    }
}
