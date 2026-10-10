using Avalonia.Media;

namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// Super Chat 与上舰记录的展示项。
/// </summary>
public class SuperChatItem
{
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
    public string GuardLevelName { get; set; } = "";

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
    public string UserName { get; set; } = "";

    /// <summary>
    /// 金额文本。
    /// </summary>
    public string PriceText { get; set; } = "";

    /// <summary>
    /// SC 边框颜色。
    /// </summary>
    public Color BorderColor { get; set; } = Color.Parse("#2A60B2");

    /// <summary>
    /// 展示时间。
    /// </summary>
    public string DisplayTime => Time.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>
    /// 详情行。
    /// </summary>
    public string DetailLine => string.IsNullOrWhiteSpace(PriceText) ? DisplayTime : $"{DisplayTime} · {PriceText}";

    /// <summary>
    /// SC 内容。
    /// </summary>
    public string Content { get; set; } = "";
}
