namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站直播 Super Chat。
/// </summary>
public class BilibiliSuperChatMessage
{
    /// <summary>
    /// SC 消息 ID。
    /// </summary>
    public string MessageId { get; set; } = "";

    /// <summary>
    /// 直播间 ID。
    /// </summary>
    public string RoomId { get; set; } = "";

    /// <summary>发送者 UID，协议缺失时为 0。</summary>
    public long Uid { get; set; }

    /// <summary>
    /// 用户名。
    /// </summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// 金额。
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 金额文本。
    /// </summary>
    public string PriceText { get; set; } = "";

    /// <summary>
    /// B 站 SC 边框颜色。
    /// </summary>
    public string BorderColor { get; set; } = "#2A60B2";

    /// <summary>
    /// SC 内容。
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// 时间戳。
    /// </summary>
    public long Timestamp { get; set; }
}
