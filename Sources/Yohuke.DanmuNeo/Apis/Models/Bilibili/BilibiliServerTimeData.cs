using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站当前服务器时间。
/// </summary>
public sealed class BilibiliServerTimeData
{
    /// <summary>
    /// 当前 UTC 的 Unix 秒数。
    /// </summary>
    [JsonProperty("now")]
    public long Now { get; set; }
}
