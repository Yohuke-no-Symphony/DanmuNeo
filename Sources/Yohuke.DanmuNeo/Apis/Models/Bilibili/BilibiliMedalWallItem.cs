using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>粉丝牌墙的一项。</summary>
public sealed class BilibiliMedalWallItem
{
    /// <summary>牌子信息。</summary>
    [JsonProperty("medal_info")]
    public BilibiliMedalInfo? MedalInfo { get; set; }
    /// <summary>所属主播昵称。</summary>
    [JsonProperty("target_name")]
    public string? TargetName { get; set; }
}
