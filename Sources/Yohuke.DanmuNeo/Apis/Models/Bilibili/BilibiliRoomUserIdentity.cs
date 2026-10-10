using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// 直播间用户接口返回的登录身份。
/// </summary>
public class BilibiliRoomUserIdentity
{
    /// <summary>
    /// 登录用户 UID。
    /// </summary>
    [JsonProperty("uid")]
    public long Uid { get; set; }
}
