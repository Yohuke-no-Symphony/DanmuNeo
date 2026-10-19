using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>B站粉丝牌墙返回的数据与完整性提示。</summary>
public sealed class BilibiliMedalWallData
{
    /// <summary>账号拥有的牌子；缺失时不能解释为空列表。</summary>
    [JsonProperty("list")]
    public List<BilibiliMedalWallItem>? List { get; set; }
    /// <summary>服务器报告的牌子总量。</summary>
    [JsonProperty("count")]
    public int? Count { get; set; }
    /// <summary>兼容服务器提供的总数。</summary>
    [JsonProperty("total")]
    public int? Total { get; set; }
    /// <summary>是否仍有未返回的数据。</summary>
    [JsonProperty("has_more")]
    public bool? HasMore { get; set; }
}
