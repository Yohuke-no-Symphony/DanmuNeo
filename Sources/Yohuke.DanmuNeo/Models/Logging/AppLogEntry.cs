using System.Globalization;

namespace Yohuke.DanmuNeo.Models.Logging;

/// <summary>
/// 带有稳定序号和诊断详情的不可变日志记录。
/// </summary>
/// <param name="Sequence">会话内单调递增的序号。</param>
/// <param name="Timestamp">记录时间。</param>
/// <param name="Level">严重程度。</param>
/// <param name="Source">操作来源及可选房间上下文。</param>
/// <param name="Message">简短结果说明。</param>
/// <param name="Details">已经脱敏的完整诊断详情。</param>
public sealed record AppLogEntry(long Sequence, DateTimeOffset Timestamp, AppLogLevel Level,
    string Source, string Message, string Details)
{
    /// <summary>用于列表展示的本地时间和操作摘要。</summary>
    public string Summary => string.Create(CultureInfo.InvariantCulture,
        $"{Timestamp.ToLocalTime():HH:mm:ss.fff}  {Level}  [{Source}] {Message}");

    /// <summary>供搜索与复制使用的完整记录，包含折叠的诊断详情。</summary>
    public string FullText => string.Create(CultureInfo.InvariantCulture,
        $"{Timestamp:O}  {Level}  [{Source}] {Message}") +
        (HasDetails ? Environment.NewLine + Details : string.Empty);

    /// <summary>是否存在可展开的诊断详情。</summary>
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    /// <summary>是否为错误记录。</summary>
    public bool IsError => Level == AppLogLevel.ERROR;
    /// <summary>是否为警告记录。</summary>
    public bool IsWarning => Level == AppLogLevel.WARNING;
}
