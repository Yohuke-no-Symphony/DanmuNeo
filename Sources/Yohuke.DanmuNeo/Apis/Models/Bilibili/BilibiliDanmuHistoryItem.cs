using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>服务器历史弹幕条目。</summary>
public class BilibiliDanmuHistoryItem
{
    /// <summary>发送者 UID。</summary>
    [JsonProperty("uid")]
    public long Uid { get; set; }

    /// <summary>发送者名称。</summary>
    [JsonProperty("nickname")]
    public string Nickname { get; set; } = "";

    /// <summary>原始正文。</summary>
    [JsonProperty("text")]
    public string Text { get; set; } = "";

    /// <summary>服务器消息标识。</summary>
    [JsonProperty("id")]
    public string MessageId { get; set; } = "";

    /// <summary>服务器时间。</summary>
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    /// <summary>北京时间文本。</summary>
    [JsonProperty("timeline")]
    public string Timeline { get; set; } = "";

    /// <summary>房管标记。</summary>
    [JsonProperty("isadmin")]
    public JToken? IsAdmin { get; set; }

    /// <summary>用户与粉丝牌信息。</summary>
    [JsonProperty("user")]
    public JObject? User { get; set; }

    /// <summary>文字表情映射。</summary>
    [JsonProperty("emots")]
    public JObject? Emots { get; set; }

    /// <summary>整条表情信息。</summary>
    [JsonProperty("emoticon")]
    public JObject? Emoticon { get; set; }
}
