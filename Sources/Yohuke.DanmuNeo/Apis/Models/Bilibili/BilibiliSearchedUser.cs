using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>用户搜索结果中的身份。</summary>
public sealed class BilibiliSearchedUser
{
    /// <summary>用户 UID。</summary>
    [JsonProperty("mid")]
    public long Uid { get; set; }

    /// <summary>可能带有高亮标记的昵称。</summary>
    [JsonProperty("uname")]
    public string? Name { get; set; }
}
