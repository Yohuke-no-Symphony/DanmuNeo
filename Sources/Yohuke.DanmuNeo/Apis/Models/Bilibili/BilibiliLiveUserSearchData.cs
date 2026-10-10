using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站直播用户搜索数据。
/// </summary>
public class BilibiliLiveUserSearchData
{
    /// <summary>
    /// 搜索结果。
    /// </summary>
    [JsonProperty("result")]
    public List<BilibiliLiveUser>? Result { get; set; }

    /// <summary>
    /// 总页数。
    /// </summary>
    [JsonProperty("numPages")]
    public int TotalPages { get; set; }

    /// <summary>
    /// 未显式映射的字段。
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtraData { get; set; }
}
