using System.Globalization;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站直播大航海购买或续费消息。
/// </summary>
public class BilibiliGuardPurchaseMessage
{
    /// <summary>
    /// 服务器提供的消息 ID；协议未提供时为空。
    /// </summary>
    public string MessageId { get; set; } = "";

    /// <summary>
    /// 直播间 ID。
    /// </summary>
    public string RoomId { get; set; } = "";

    /// <summary>
    /// 购买用户 UID。
    /// </summary>
    public long Uid { get; set; }

    /// <summary>
    /// 购买用户名。
    /// </summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// 大航海档位：1 为总督，2 为提督，3 为舰长。
    /// </summary>
    public int GuardLevel { get; set; }

    /// <summary>
    /// 大航海档位名称。
    /// </summary>
    public string GuardLevelName => GuardLevel switch
    {
        1 => "总督",
        2 => "提督",
        3 => "舰长",
        _ => ""
    };

    /// <summary>
    /// 服务器事件时间的 Unix 秒；协议未提供时为 0。
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// 本次购买数量；协议未提供时为 0。
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// 是否为团体大航海记录。
    /// </summary>
    public bool IsGroup { get; set; }

    /// <summary>
    /// 协议提供的接收者 UID。
    /// </summary>
    public long ReceiverUid { get; set; }

    /// <summary>
    /// 优先使用真实服务器消息 ID 的去重键；无 ID 时使用服务器时间与购买信息，缺少可靠字段时为空。
    /// </summary>
    public string Identity => !string.IsNullOrWhiteSpace(MessageId)
        ? $"guard:{RoomId}:id:{MessageId}"
        : Timestamp > 0 && Uid > 0
            ? string.Create(CultureInfo.InvariantCulture, $"guard:{RoomId}:{Uid}:{GuardLevel}:{Timestamp}:{Quantity}")
            : "";
}
