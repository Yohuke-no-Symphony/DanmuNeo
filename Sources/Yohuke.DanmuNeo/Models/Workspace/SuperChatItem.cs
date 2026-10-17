using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// Super Chat 与上舰记录的展示项。
/// </summary>
public class SuperChatItem : ObservableObject
{
    private string guardLevelName = "";
    private string userName = "";
    private string priceText = "";
    private string content = "";

    /// <summary>
    /// 付费事件类型。
    /// </summary>
    public LivePaidEventKind Kind { get; set; }

    /// <summary>
    /// 是否为只展示用户名和档位的上舰记录。
    /// </summary>
    public bool IsGuardPurchase => Kind == LivePaidEventKind.GuardPurchase;

    /// <summary>
    /// 大航海档位名称；仅上舰记录使用。
    /// </summary>
    public string GuardLevelName
    {
        get => GuardLevel is >= 1 and <= 3
            ? GuardPurchaseStyleService.GetLevelName(GuardLevel)
            : guardLevelName;
        set
        {
            if (SetProperty(ref guardLevelName, value))
            {
                OnPropertyChanged(nameof(BadgeText));
            }
        }
    }

    /// <summary>
    /// 大航海档位；保留数值以支持现有记录即时切换语言。
    /// </summary>
    public int GuardLevel { get; set; }

    /// <summary>
    /// 事件右侧显示的金额或档位。
    /// </summary>
    public string BadgeText => IsGuardPurchase ? GuardLevelName : PriceText;

    /// <summary>
    /// 是否具有可插入同传输入框的留言内容。
    /// </summary>
    public bool CanInsertContent => Kind == LivePaidEventKind.SuperChat;

    /// <summary>
    /// SC 消息 ID。
    /// </summary>
    public string MessageId { get; set; } = "";

    /// <summary>
    /// 发送时间。
    /// </summary>
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 用户名。
    /// </summary>
    public string UserName
    {
        get => userName;
        set => SetProperty(ref userName, value);
    }

    /// <summary>
    /// 金额文本。
    /// </summary>
    public string PriceText
    {
        get => BatteryAmount is { } amount ? Localization.Format("Preview.BatteryAmount", amount) : priceText;
        set
        {
            if (SetProperty(ref priceText, value))
            {
                OnPropertyChanged(nameof(BadgeText));
                OnPropertyChanged(nameof(DetailLine));
            }
        }
    }

    /// <summary>
    /// 应用生成的 SC 预览档位金额；真实消息保持服务器提供的金额文本。
    /// </summary>
    public int? BatteryAmount { get; set; }

    /// <summary>
    /// SC 边框颜色。
    /// </summary>
    public Color BorderColor { get; set; } = Color.Parse("#2A60B2");

    /// <summary>
    /// 展示时间。
    /// </summary>
    public string DisplayTime => Time.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>
    /// 刷新由档位生成的显示文字。
    /// </summary>
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(GuardLevelName));
        OnPropertyChanged(nameof(PriceText));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(DetailLine));
    }

    /// <summary>
    /// 详情行。
    /// </summary>
    public string DetailLine => string.IsNullOrWhiteSpace(PriceText) ? DisplayTime : $"{DisplayTime} · {PriceText}";

    /// <summary>
    /// SC 内容。
    /// </summary>
    public string Content
    {
        get => content;
        set => SetProperty(ref content, value);
    }
}
