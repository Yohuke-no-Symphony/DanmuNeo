using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>用户资料中的直播间关联。</summary>
public class BilibiliUserLiveRoom
{
    /// <summary>直播间 ID；零表示尚未开通。</summary>
    [JsonProperty("roomid")]
    public long RoomId { get; set; }

    /// <summary>直播状态。</summary>
    [JsonProperty("liveStatus")]
    public int LiveStatus { get; set; }
}
