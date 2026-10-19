using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站用户公开资料。
/// </summary>
public class BilibiliUserCardData
{
    /// <summary>
    /// 用户 UID。
    /// </summary>
    [JsonProperty("mid")]
    public long Uid { get; set; }

    /// <summary>
    /// 用户名。
    /// </summary>
    [JsonProperty("name")]
    public string? Name { get; set; }

    /// <summary>
    /// 头像链接。
    /// </summary>
    [JsonProperty("face")]
    public string? Face { get; set; }

    /// <summary>用户关联的直播间；未开通时为空。</summary>
    [JsonProperty("live_room")]
    public BilibiliUserLiveRoom? LiveRoom { get; set; }

    /// <summary>
    /// 未显式映射的字段。
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtraData { get; set; }
}