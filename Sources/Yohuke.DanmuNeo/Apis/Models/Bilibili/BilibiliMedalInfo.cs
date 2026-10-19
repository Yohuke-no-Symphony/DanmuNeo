using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>B站粉丝牌的协议字段。</summary>
public sealed class BilibiliMedalInfo
{
    /// <summary>佩戴接口使用的牌子ID。</summary>
    [JsonProperty("medal_id")]
    public long MedalId { get; set; }
    /// <summary>真实所属主播UID。</summary>
    [JsonProperty("target_id")]
    public long TargetUid { get; set; }
    /// <summary>牌子名称。</summary>
    [JsonProperty("medal_name")]
    public string? Name { get; set; }
    /// <summary>牌子等级。</summary>
    [JsonProperty("level")]
    public int Level { get; set; }
    /// <summary>当前是否佩戴。</summary>
    [JsonProperty("wearing_status")]
    public int WearingStatus { get; set; }
}
