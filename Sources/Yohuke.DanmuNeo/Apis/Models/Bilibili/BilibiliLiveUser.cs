using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站直播用户搜索结果。
/// </summary>
public class BilibiliLiveUser
{
    /// <summary>
    /// 用户 UID。
    /// </summary>
    [JsonProperty("uid")]
    public long Uid { get; set; }

    /// <summary>
    /// 用户名。
    /// </summary>
    [JsonProperty("uname")]
    public string? Name { get; set; }

    /// <summary>
    /// 直播间 ID。
    /// </summary>
    [JsonProperty("roomid")]
    public long RoomId { get; set; }

    /// <summary>
    /// 直播标题。
    /// </summary>
    [JsonProperty("title")]
    public string? Title { get; set; }

    /// <summary>
    /// 是否正在直播。
    /// </summary>
    [JsonProperty("is_live")]
    public bool IsLive { get; set; }

    /// <summary>
    /// 未显式映射的字段。
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtraData { get; set; }
}
