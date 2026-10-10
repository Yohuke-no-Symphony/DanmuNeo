namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站直播普通弹幕。
/// </summary>
public class BilibiliDanmuMessage
{
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
    /// 直播用户等级，与粉丝牌等级独立。
    /// </summary>
    public int UserLevel { get; set; }

    /// <summary>
    /// 是否为 B 站协议标记的单表情弹幕。
    /// </summary>
    public bool IsEmoticon { get; set; }
}
