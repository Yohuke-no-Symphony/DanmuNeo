using Yohuke.DanmuNeo.Models.Logging;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 本地直播代理流条目。
/// </summary>
internal class LivePlayerStreamEntry
{
    /// <summary>
    /// 恢复前已报告的播放器故障签名。
    /// </summary>
    public HashSet<string> ReportedDiagnostics { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 按报告顺序限制故障签名缓存。
    /// </summary>
    public Queue<string> DiagnosticOrder { get; } = new();

    /// <summary>
    /// 上游请求失败已在托管代理报告。
    /// </summary>
    public bool HasUpstreamFailure { get; set; }

    /// <summary>
    /// 上游流地址。
    /// </summary>
    public string StreamUrl { get; set; } = "";

    /// <summary>
    /// 直播间 ID。
    /// </summary>
    public string RoomId { get; set; } = "";

    /// <summary>
    /// Cookie。
    /// </summary>
    public string Cookie { get; set; } = "";

    /// <summary>
    /// 取消令牌。
    /// </summary>
    public CancellationTokenSource CancellationTokenSource { get; } = new();
}
