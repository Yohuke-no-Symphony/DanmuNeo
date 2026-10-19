using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// 当前登录身份的导航资料。
/// </summary>
public sealed class BilibiliNavigationData
{
    /// <summary>
    /// 是否已登录；空值表示响应缺少必要字段。
    /// </summary>
    [JsonProperty("isLogin")]
    public bool? IsLogin { get; set; }

    /// <summary>
    /// 用户 UID。
    /// </summary>
    [JsonProperty("mid")]
    public long Uid { get; set; }

    /// <summary>
    /// 用户昵称。
    /// </summary>
    [JsonProperty("uname")]
    public string UserName { get; set; } = "";
}
