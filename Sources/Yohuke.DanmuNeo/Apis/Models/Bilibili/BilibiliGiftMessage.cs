namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站直播礼物消息。
/// </summary>
public class BilibiliGiftMessage
{
    /// <summary>
    /// 礼物消息 ID。
    /// </summary>
    public string MessageId { get; set; } = "";

    /// <summary>
    /// 直播间 ID。
    /// </summary>
    public string RoomId { get; set; } = "";

    /// <summary>
    /// 送礼用户 UID。
    /// </summary>
    public long Uid { get; set; }

    /// <summary>
    /// 送礼用户名。
    /// </summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// 礼物 ID。
    /// </summary>
    public string GiftId { get; set; } = "";

    /// <summary>
    /// 礼物名称。
    /// </summary>
    public string GiftName { get; set; } = "";

    /// <summary>
    /// 本条消息包含的礼物数量。
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// B 站连击 ID。
    /// </summary>
    public string BatchComboId { get; set; } = "";

    /// <summary>
    /// B 站报告的连击累计数量；未提供时为 0。
    /// </summary>
    public int BatchComboCount { get; set; }

    /// <summary>
    /// 单个礼物的价格，单位为千分之一元。
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// 服务器事件时间的 Unix 秒；协议未提供时为 0。
    /// </summary>
    public long Timestamp { get; set; }
}
