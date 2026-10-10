using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站用户在直播间内的信息。
/// </summary>
public class BilibiliUserInfoData
{
    /// <summary>
    /// 服务端确认的登录身份。
    /// </summary>
    [JsonProperty("info")]
    public BilibiliRoomUserIdentity? Info { get; set; }

    /// <summary>
    /// 当前房间的权限。
    /// </summary>
    [JsonProperty("badge")]
    public BilibiliRoomUserBadge? Badge { get; set; }

    /// <summary>
    /// 用户 UID。
    /// </summary>
    [JsonProperty("uid")]
    public long Uid { get; set; }

    /// <summary>
    /// 是否已登录。
    /// </summary>
    [JsonProperty("is_login")]
    public bool IsLogin { get; set; }

    /// <summary>
    /// 用户当前弹幕配置。
    /// </summary>
    [JsonProperty("property")]
    public JToken? Property { get; set; }

    /// <summary>
    /// 未显式映射的字段。
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtraData { get; set; }
}
