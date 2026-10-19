namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站直播普通弹幕。
/// </summary>
public class BilibiliDanmuMessage
{
    /// <summary>服务器提供的消息标识；没有可靠标识时为空。</summary>
    public string MessageId { get; set; } = "";

    /// <summary>服务器事件时间的 Unix 秒；协议未提供时为 0。</summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// 直播间 ID。
    /// </summary>
    public string RoomId { get; set; } = "";

    /// <summary>
    /// 用户 UID。
    /// </summary>
    public long Uid { get; set; }

    /// <summary>
    /// 用户名。
    /// </summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// 弹幕内容。
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// 弹幕正文中的表情图片映射。
    /// </summary>
    public List<BilibiliDanmuEmoticon> Emoticons { get; set; } = [];

    /// <summary>
    /// 是否为协议标记的直播间房管。
    /// </summary>
    public bool IsRoomAdmin { get; set; }

    /// <summary>
    /// 用户佩戴的粉丝牌名称。
    /// </summary>
    public string MedalName { get; set; } = "";

    /// <summary>
    /// 用户佩戴的粉丝牌等级。
    /// </summary>
    public int MedalLevel { get; set; }

    /// <summary>
    /// 佩戴粉丝牌所属主播的大航海档位；0 为普通，1 为总督，2 为提督，3 为舰长。
    /// </summary>
    public int MedalGuardLevel { get; set; }

    /// <summary>
    /// 直播用户等级，与粉丝牌等级独立。
    /// </summary>
    public int UserLevel { get; set; }

    /// <summary>
    /// 是否为 B 站协议标记的单表情弹幕。
    /// </summary>
    public bool IsEmoticon { get; set; }
}
