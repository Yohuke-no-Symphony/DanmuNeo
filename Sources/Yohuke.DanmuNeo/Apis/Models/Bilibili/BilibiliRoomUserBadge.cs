using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// 直播间内的用户权限标记。
/// </summary>
public class BilibiliRoomUserBadge
{
    /// <summary>
    /// 是否为当前房间管理员。
    /// </summary>
    [JsonProperty("is_room_admin")]
    public bool IsRoomAdmin { get; set; }
}
