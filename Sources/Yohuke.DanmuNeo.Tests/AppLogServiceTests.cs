using System.Threading;
using Yohuke.DanmuNeo.Models.Logging;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证有界日志、后台并发、持续故障与敏感诊断脱敏。
/// </summary>
public class AppLogServiceTests
{
    /// <summary>发送诊断与真实异常链合入同一条错误，且两部分均经过脱敏。</summary>
    [Fact]
    public void ErrorPreservesDiagnosticsAndExceptionWithoutCredentials()
    {
        using var log = new AppLogService();
        var error = new IOException("网络失败 buvid4=error-secret", new InvalidOperationException("内部原因"));

        log.Error("发送", "发送失败", error, "code=0 message=f buvid_fp=diagnostic-secret");

        var entry = Assert.Single(log.Entries);
        Assert.Contains("code=0 message=f", entry.Details);
        Assert.Contains(nameof(IOException), entry.Details);
        Assert.Contains("内部原因", entry.Details);
        Assert.DoesNotContain("diagnostic-secret", entry.FullText);
        Assert.DoesNotContain("error-secret", entry.FullText);
    }

    /// <summary>已经脱敏的响应再次进入日志时保持隐藏标识稳定。</summary>
    [Theory]
    [InlineData("csrf=secret")]
    [InlineData("buvid_fp=secret")]
    [InlineData("Cookie: SESSDATA=secret")]
    public void RepeatedSanitizationKeepsRedactedTextStable(string text)
    {
        var sanitized = AppLogSanitizer.Sanitize(text);

        Assert.Equal(sanitized, AppLogSanitizer.Sanitize(sanitized));
    }

    /// <summary>仅保留最近记录，未读数跟随仍保留的错误且序号不会复用。</summary>
    [Fact]
    public void CapacityEvictsOldErrorsAndReadMarkersStayMonotonic()
    {
        using var log = new AppLogService(3);
        log.Error("发送 / 房间 100", "发送失败");
        log.Information("保存", "已保存");
        log.Warning("监听", "重试中");
        var snapshot = log.Entries;
        Assert.Equal(1, log.UnreadErrorCount);

        log.Information("布局", "已恢复");

        Assert.Equal(new long[] { 2, 3, 4 }, log.Entries.Select(entry => entry.Sequence));
        Assert.Equal(0, log.UnreadErrorCount);
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(1, snapshot[0].Sequence);

        log.Error("发送", "第二次失败");
        log.MarkRead();
        Assert.Equal(0, log.UnreadErrorCount);
        log.Error("发送", "第三次失败");
        Assert.Equal(1, log.UnreadErrorCount);
        log.Clear();
        Assert.Empty(log.Entries);
        Assert.Equal(0, log.UnreadErrorCount);
        log.Error("发送", "第四次失败");
        Assert.Equal(7, Assert.Single(log.Entries).Sequence);
    }

    /// <summary>保留完整异常链，并仅对恢复前的相同持续故障去重。</summary>
    [Fact]
    public void DiagnosticsDeduplicateAcrossClearAndResetAfterRecovery()
    {
        using var log = new AppLogService();
        var first = new IOException("连接断开", new InvalidOperationException("内部原因"));
        log.SetDiagnosticError("监听 / 房间 100", first);
        log.SetDiagnosticError("监听 / 房间 100", first);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(AppLogLevel.ERROR, entry.Level);
        Assert.Contains("System.IO.IOException", entry.FullText);
        Assert.Contains("内部原因", entry.Details);
        Assert.Contains("房间 100", entry.Source);
        Assert.True(entry.HasDetails);

        log.Clear();
        log.SetDiagnosticError("监听 / 房间 100", first);
        Assert.Empty(log.Entries);
        log.SetDiagnosticError("监听 / 房间 100", null);
        log.SetDiagnosticError("监听 / 房间 100", first);
        Assert.Single(log.Entries);
        log.SetDiagnosticError("监听 / 房间 100", new IOException("新的故障"));
        Assert.Equal(2, log.Entries.Count);
        log.SetDiagnosticError("监听 / 房间 101", first);
        Assert.Equal(3, log.Entries.Count);
    }

    /// <summary>每次独立操作错误均保留，不受持续故障去重影响。</summary>
    [Fact]
    public void IndependentOperationErrorsAreNotDeduplicated()
    {
        using var log = new AppLogService();
        var error = new IOException("拒绝请求");
        log.Error("发送", "发送失败", error);
        log.Error("发送", "发送失败", error);
        Assert.Equal(2, log.Entries.Count);
        Assert.Equal(2, log.UnreadErrorCount);
    }

    /// <summary>并发追加得到唯一有序序号，事件处理程序可从其他线程读取日志。</summary>
    [Fact]
    public void ConcurrentWritersRaiseEventsOutsideLock()
    {
        using var log = new AppLogService(240);
        var notifications = 0;
        log.Changed += (_, _) => Interlocked.Increment(ref notifications);
        Parallel.For(0, 240, value => log.Information("并发测试", value.ToString()));
        Assert.Equal(240, notifications);
        Assert.Equal(Enumerable.Range(1, 240).Select(value => (long)value),
            log.Entries.Select(entry => entry.Sequence));

        var readable = false;
        log.Changed += (_, _) =>
        {
            readable = Task.Run(() => log.Entries.Count).Wait(TimeSpan.FromSeconds(3));
        };
        log.Information("事件", "检查锁外调用");
        Assert.True(readable);
    }

    /// <summary>释放后忽略迟到的后台报告，不触发订阅且保留已有快照。</summary>
    [Fact]
    public void DisposeIgnoresLateCallbacksAndReleasesSubscribers()
    {
        var log = new AppLogService();
        log.Information("保存", "已保存");
        var notifications = 0;
        log.Changed += (_, _) => notifications++;
        log.Dispose();
        log.Dispose();
        log.Information("保存", "迟到的完成");
        log.Warning("监听", "迟到的重试");
        log.Error("发送", "迟到的异常");
        log.SetDiagnosticError("监听", new IOException("迟到的故障"));
        log.Clear();
        log.MarkRead();
        Assert.Equal(0, notifications);
        Assert.Single(log.Entries);
    }

    /// <summary>摘要、来源和完整异常详情中的凭据均在进入内存日志前脱敏。</summary>
    [Theory]
    [InlineData("Cookie: SESSDATA=secret-sample; bili_jct=csrf-sample", "secret-sample")]
    [InlineData("Cookie: SESSDATA=session-sample; unknown_cookie=tail-sample", "tail-sample")]
    [InlineData("Cookie: [已隐藏]; unknown_cookie=tail-sample", "tail-sample")]
    [InlineData("Authorization: Basic basic-sample", "basic-sample")]
    [InlineData("Authorization: Bearer bearer-sample", "bearer-sample")]
    [InlineData("{\"cookie\":\"SESSDATA=json-sample; bili_jct=another\"}", "json-sample")]
    [InlineData("csrf=csrf-sample", "csrf-sample")]
    [InlineData("buvid4=device-sample", "device-sample")]
    [InlineData("buvid_fp=fingerprint-sample", "fingerprint-sample")]
    [InlineData("token='quoted-sample'", "quoted-sample")]
    [InlineData("https://live.example.test/play.flv?deadline=123&sign=url-sample#fragment", "url-sample")]
    [InlineData("https://user:userinfo-sample@example.test/play.flv", "userinfo-sample")]
    [InlineData("https:\\/\\/example.test/login?gourl=redirect-sample", "redirect-sample")]
    public void SanitizesCredentialsInEveryEntryField(string sensitiveText, string secret)
    {
        using var log = new AppLogService();
        log.Error(sensitiveText, sensitiveText, new IOException(sensitiveText));
        var entry = Assert.Single(log.Entries);
        Assert.DoesNotContain(secret, entry.FullText);
        Assert.Contains("已隐藏", entry.FullText);
    }

    /// <summary>本地播放器代理与诊断端点的路径会话凭据在普通、JSON 和堆栈内容中均隐藏。</summary>
    [Theory]
    [InlineData("http://127.0.0.1:19200/stream/local-session-secret?v=123", "/stream/")]
    [InlineData("http://127.0.0.2:19200/diagnostic/local-session-secret", "/diagnostic/")]
    [InlineData("http://localhost:19200/stream/local-session-secret", "/stream/")]
    [InlineData("http://[::1]:19200/diagnostic/local-session-secret?v=123", "/diagnostic/")]
    [InlineData("{\"nested\":{\"url\":\"http:\\/\\/127.0.0.1:19200\\/stream\\/local-session-secret?v=123\"}}", "/stream/")]
    [InlineData("Error: failed fetch http://localhost:19200/diagnostic/local-session-secret\n    at player.js:42", "/diagnostic/")]
    public void SanitizesLoopbackSessionPathsAcrossEntryAndSharedSanitizer(string text, string route)
    {
        var sanitized = AppLogSanitizer.Sanitize(text);
        Assert.DoesNotContain("local-session-secret", sanitized);
        Assert.Contains(route + "[已隐藏]", sanitized);

        using var log = new AppLogService();
        log.Error("播放器", text, new IOException(text));
        var entry = Assert.Single(log.Entries);
        Assert.DoesNotContain("local-session-secret", entry.FullText);
        Assert.Contains(route + "[已隐藏]", entry.FullText);
    }

    /// <summary>远程网址路径和本地普通资源路径保持不变，仅移除 URL 查询凭据。</summary>
    [Theory]
    [InlineData("https://live.example.test/stream/ordinary-resource", "https://live.example.test/stream/ordinary-resource")]
    [InlineData("https://live.example.test/diagnostic/ordinary-resource?v=123", "https://live.example.test/diagnostic/ordinary-resource?[已隐藏]")]
    [InlineData("http://localhost:19200/assets/player.js", "http://localhost:19200/assets/player.js")]
    public void KeepsRemoteAndOrdinaryLocalPaths(string text, string expected)
    {
        Assert.Equal(expected, AppLogSanitizer.Sanitize(text));
    }

    /// <summary>普通来源、房间号和异常堆栈不因脱敏而丢失。</summary>
    [Fact]
    public void KeepsActionRoomAndStackDetails()
    {
        using var log = new AppLogService();

        try
        {
            throw new InvalidOperationException("连接超时，房间 100");
        }
        catch (Exception error)
        {
            log.Error("监听 / 房间 100", "监听失败", error);
        }

        var entry = Assert.Single(log.Entries);
        Assert.Contains("监听 / 房间 100", entry.Summary);
        Assert.Contains(nameof(KeepsActionRoomAndStackDetails), entry.Details);
        Assert.Contains("连接超时", entry.FullText);
    }
}
