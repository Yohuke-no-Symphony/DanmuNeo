using System.IO.Compression;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.Models.Logging;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证收到消息留档的落盘、隔离、容量与故障边界。</summary>
public class ReceivedDanmuArchiveServiceTests
{
    /// <summary>文本按日期与会话独立保存，缓存清理及配置备份不涉及留档。</summary>
    [Fact]
    public async Task WritesReadableUtf8RotatesByDateAndKeepsIndependentSessions()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var directories = new AppDirectoryService(directory);
            using var log = new AppLogService();
            await using (var archive = new ReceivedDanmuArchiveService(directories, log))
            {
                var first = archive.OpenSession("100", "workspace-a", "room-a");
                var second = archive.OpenSession("100", "workspace-b", "room-b");
                Assert.True(archive.TryAppend(first, Entry("你好\n第二行")));
                Assert.True(archive.TryAppend(second, Entry("另外的工作区")));
                Assert.True(archive.TryAppend(first, Entry("历史", true) with { ReceivedAt = new(2026, 10, 20, 1, 0, 0, TimeSpan.Zero) }));
                await archive.CompleteSessionAsync(first);
                Assert.False(archive.TryAppend(first, Entry("已经停止")));
            }

            var files = Directory.GetFiles(directories.DanmuLogsDirectory, "*.log", SearchOption.AllDirectories);
            Assert.Equal(3, files.Length);
            var text = string.Join("\n", files.Select(File.ReadAllText));
            Assert.Contains("你好\\n第二行", text);
            Assert.Contains("历史", text);
            Assert.Contains("另外的工作区", text);
            Assert.DoesNotContain("已经停止", text);
            var backups = new AppBackupService(directories);
            backups.ClearCache();
            Assert.Equal(3, Directory.GetFiles(directories.DanmuLogsDirectory, "*.log", SearchOption.AllDirectories).Length);
            var backup = await backups.ExportBackupZipAsync(Path.Combine(directory, "Exports"));
            using var zip = ZipFile.OpenRead(backup);
            Assert.DoesNotContain(zip.Entries, item => item.FullName.Contains("DanmuLogs"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>磁盘写入失败进入错误日志并终止该会话后续写入。</summary>
    [Fact]
    public async Task WriterFailureIsVisibleAndDoesNotThrowFromAppend()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var log = new AppLogService();
        await using var service = new ReceivedDanmuArchiveService(new(directory), log,
            writerFactory: _ => throw new IOException("disk full"));
        var session = service.OpenSession("100", "workspace", "room");
        Assert.True(service.TryAppend(session, Entry("内容")));
        await service.FlushAsync();
        Assert.Contains(log.Entries, entry => entry.Level == AppLogLevel.ERROR);
        Assert.False(service.TryAppend(session, Entry("无法继续")));
    }

    /// <summary>队列溢出可见且已经接受的记录仍会落盘。</summary>
    [Fact]
    public async Task OverflowReportsIncompleteSessionAndFlushesAcceptedRecords()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var log = new AppLogService();
            var writer = new BlockingArchiveTextWriter();
            await using var service = new ReceivedDanmuArchiveService(new(directory), log, 1, _ => writer);
            var session = service.OpenSession("100", "workspace", "room");
            Assert.True(service.TryAppend(session, Entry("first")));
            await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(service.TryAppend(session, Entry("second")));
            Assert.False(service.TryAppend(session, Entry("overflow")));
            Assert.Contains(log.Entries, entry => entry.Level == AppLogLevel.ERROR);
            writer.Release.TrySetResult();
            await service.CompleteSessionAsync(session);
            Assert.Contains("first", writer.ToString());
            Assert.Contains("second", writer.ToString());
            Assert.DoesNotContain("overflow", writer.ToString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static ReceivedDanmuLogEntry Entry(string content, bool history = false)
    {
        return new("弹幕", "42", "用户", content, new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero), 1800000000, history);
    }
}
