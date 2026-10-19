using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>全部用户昵称搜索分页。</summary>
public sealed class BilibiliUserSearchData
{
    /// <summary>结果列表；缺失时保留空值。</summary>
    [JsonProperty("result")]
    public List<BilibiliSearchedUser>? Result { get; set; }

    /// <summary>总页数。</summary>
    [JsonProperty("numPages")]
    public int? TotalPages { get; set; }
}
