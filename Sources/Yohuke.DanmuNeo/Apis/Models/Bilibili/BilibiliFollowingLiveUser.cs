using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>当前正在直播的关注用户。</summary>
public class BilibiliFollowingLiveUser
{
    /// <summary>主播 UID。</summary>
    [JsonProperty("mid")]
    public long Uid { get; set; }

    /// <summary>直播间 ID。</summary>
    [JsonProperty("room_id")]
    public long RoomId { get; set; }

    /// <summary>主播昵称。</summary>
    [JsonProperty("uname")]
    public string? Name { get; set; }

    /// <summary>主播头像。</summary>
    [JsonProperty("face")]
    public string? Face { get; set; }
}
