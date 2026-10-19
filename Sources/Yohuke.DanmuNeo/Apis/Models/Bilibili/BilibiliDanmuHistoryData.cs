using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>近期普通弹幕响应。</summary>
public class BilibiliDanmuHistoryData
{
    /// <summary>直播间普通弹幕。</summary>
    [JsonProperty("room")]
    public List<BilibiliDanmuHistoryItem> Room { get; set; } = [];
}
