using System.Diagnostics;
using Avalonia.Logging;
using Yohuke.DanmuNeo.Models.Logging;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证运行时日志适配器保留真实级别、详情并遵守会话释放边界。
/// </summary>
public class AppLogBridgeTests
{
    /// <summary>
    /// Trace 的错误与警告不丢失级别，敏感参数在记录前脱敏。
    /// </summary>
    [Fact]
    public void TraceKeepsLevelsRedactsAndStopsAfterDispose()
    {
        using var log = new AppLogService();
        using var listener = new AppLogTraceListener(log);
        listener.TraceEvent(null, "测试", TraceEventType.Warning, 0, "warning");
        listener.TraceEvent(null, "测试", TraceEventType.Error, 0, "token={0}", "test-secret");
        listener.Write("ordinary ");
        listener.WriteLine("message");

        Assert.Equal(new[] { AppLogLevel.WARNING, AppLogLevel.ERROR, AppLogLevel.INFO },
            log.Entries.Select(entry => entry.Level));
        Assert.DoesNotContain("test-secret", log.Entries[1].FullText);
        Assert.Equal("ordinary message", log.Entries[2].Message);
        listener.Dispose();
        listener.TraceEvent(null, "测试", TraceEventType.Error, 0, "late");
        listener.WriteLine("late");
        Assert.Equal(3, log.Entries.Count);
    }

    /// <summary>
    /// Avalonia 的结构化异常与警告进入 Log，调试信息不挤占业务日志。
    /// </summary>
    [Fact]
    public void AvaloniaSinkPreservesStructuredErrorAndFiltersDebug()
    {
        using var log = new AppLogService();
        var sink = new AppLogAvaloniaSink(log);
        var error = new InvalidOperationException("outer", new IOException("inner"));
        sink.Log(LogEventLevel.Debug, "Binding", null, "debug");
        sink.Log(LogEventLevel.Warning, "Binding", null, "属性 {Property} 无效", "Test");
        sink.Log(LogEventLevel.Error, "Rendering", null, "渲染失败：{Error}", error);

        Assert.Equal(2, log.Entries.Count);
        Assert.Equal(AppLogLevel.WARNING, log.Entries[0].Level);
        Assert.Contains("Test", log.Entries[0].Message);
        Assert.Equal("Avalonia/Rendering", log.Entries[1].Source);
        Assert.Contains("inner", log.Entries[1].Details);
        Assert.Contains(nameof(IOException), log.Entries[1].Details);
    }
}
