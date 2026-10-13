using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站直播间弹幕配置数据。
/// </summary>
public class BilibiliDanmuConfigData
{
    /// <summary>
    /// 按权限分组的颜色列表。
    /// </summary>
    [JsonProperty("group")]
    public JToken? Group { get; set; }

    /// <summary>
    /// 最大弹幕长度。
    /// </summary>
    [JsonProperty("length")]
    public int Length { get; set; }

    /// <summary>
    /// 可用颜色列表。
    /// </summary>
    [JsonProperty("color")]
    public JToken? Color { get; set; }

    /// <summary>
    /// 可用模式列表。
    /// </summary>
    [JsonProperty("mode")]
    public JToken? Mode { get; set; }

    /// <summary>
    /// 未显式映射的字段。
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken>? ExtraData { get; set; }
}
