using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>动态入口的开播关注集合。</summary>
public class BilibiliFollowingLiveUsers
{
    /// <summary>当前接口返回的开播关注，接口不提供分页。</summary>
    [JsonProperty("items")]
    public List<BilibiliFollowingLiveUser>? Items { get; set; }
}
