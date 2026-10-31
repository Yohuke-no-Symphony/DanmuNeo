using Yohuke.DanmuNeo.Models.Logging;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 供全局操作和后台任务共用的有界会话日志。
/// </summary>
public interface IAppLogService : IDisposable
{
    /// <summary>记录或未读状态变化后在写入线程通知，处理程序在日志锁外调用。</summary>
    event EventHandler? Changed;
    /// <summary>按记录顺序返回当前保留日志的快照。</summary>
    IReadOnlyList<AppLogEntry> Entries { get; }
    /// <summary>当前保留记录中尚未阅读的错误数量。</summary>
    int UnreadErrorCount { get; }

    /// <summary>记录已完成的操作或普通状态。</summary>
    void Information(string source, string message, string? details = null);

    /// <summary>记录需要注意但不阻止运行的状态。</summary>
    void Warning(string source, string message, string? details = null);

    /// <summary>记录操作失败、可选的异常链及诊断详情。</summary>
    void Error(string source, string message, Exception? error = null, string? details = null);

    /// <summary>更新持续故障；相同故障在恢复前仅记录一次。</summary>
    void SetDiagnosticError(string source, Exception? error);

    /// <summary>将当前已有的错误标为已读。</summary>
    void MarkRead();

    /// <summary>清空记录和未读数，保留持续故障的去重状态。</summary>
    void Clear();
}
