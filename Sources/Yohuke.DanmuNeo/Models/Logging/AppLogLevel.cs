namespace Yohuke.DanmuNeo.Models.Logging;

/// <summary>
/// 应用会话日志的严重程度。
/// </summary>
public enum AppLogLevel
{
    /// <summary>已完成操作和普通状态。</summary>
    INFO,
    /// <summary>需要注意但不阻止运行的状态。</summary>
    WARNING,
    /// <summary>失败操作或持续故障。</summary>
    ERROR
}
