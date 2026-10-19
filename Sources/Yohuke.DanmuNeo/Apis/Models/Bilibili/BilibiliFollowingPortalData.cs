using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>动态入口返回的正在直播关注列表。</summary>
public class BilibiliFollowingPortalData
{
    /// <summary>开播关注数据。</summary>
    [JsonProperty("live_users")]
    public BilibiliFollowingLiveUsers? LiveUsers { get; set; }
}
