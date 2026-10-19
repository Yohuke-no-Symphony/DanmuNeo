namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 本地直播播放器会话。
/// </summary>
public class LivePlayerSession
{
    /// <summary>
    /// 会话令牌。
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// 播放器页面地址。
    /// </summary>
    public string PlayerUrl { get; set; } = "";

    /// <summary>从取流结果保留的 Unix 开播时间，未知为零。</summary>
    public long LiveTime { get; set; }
}
