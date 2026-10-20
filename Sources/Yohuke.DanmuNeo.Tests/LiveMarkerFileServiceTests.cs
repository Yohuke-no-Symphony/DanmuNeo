using System.Globalization;
using System.Threading;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证旧记录兼容、独立时间、原子文件替换与应用内写入租约。</summary>
public sealed class LiveMarkerFileServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "danmu-marker-files", Guid.NewGuid().ToString("N"));

    /// <summary>创建测试专用目录，所有文件操作限制在此目录。</summary>
    public LiveMarkerFileServiceTests()
    {
        Directory.CreateDirectory(directory);
    }

    /// <summary>即便两个时间不一致或字段顺序不同，读取、快照及保存都保持原值。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentTimesRoundTripInEitherFieldOrder(bool liveTimeFirst)
    {
        var path = Path.Combine(directory, "legacy.yaml");
        var timeFields = liveTimeFirst
            ? "  LiveTime: 00:04:00\n  RealDateTime: 2026-10-20T19:55:00+08:00"
            : "  RealDateTime: 2026-10-20T19:55:00+08:00\n  LiveTime: 00:04:00";
        await File.WriteAllTextAsync(path,
            $"StartTime: 2026-10-20T18:00:00+08:00\nMarker:\n- Message: old\n{timeFields}\n  MarkerColor: Magenta\n");
        var service = new LiveMarkerFileService();
        var original = await service.LoadAsync(path);
        var entry = Assert.Single(original.Marker);
        var originalRealTime = DateTimeOffset.Parse("2026-10-20T19:55:00+08:00", CultureInfo.InvariantCulture);
        Assert.True(entry.RealDateTime.EqualsExact(originalRealTime));
        Assert.Equal(TimeSpan.FromMinutes(4), entry.LiveTime);
        Assert.NotEqual(original.StartTime + entry.LiveTime, entry.RealDateTime);
        var edited = original with
        {
            Marker = [entry with { Message = "edited\twith\nnewlines \"quoted\" 日本語" }]
        };

        await service.SaveAsync(edited, path);
        var reloaded = await service.LoadAsync(path);
        var actual = Assert.Single(reloaded.Marker);

        Assert.True(actual.RealDateTime.EqualsExact(originalRealTime));
        Assert.Equal(entry.LiveTime, actual.LiveTime);
        Assert.Equal(edited.Marker[0].Message, actual.Message);
        Assert.Equal(LiveMarkerColor.Magenta, actual.MarkerColor);
        Assert.True(reloaded.StartTime.EqualsExact(original.StartTime));
        Assert.Equal("old", entry.Message);
    }

    /// <summary>带 offset 或 UTC 标识的日期保留时刻与 offset，新文件始终输出明确 offset。</summary>
    [Theory]
    [InlineData("2026-10-20T18:00:00+08:00")]
    [InlineData("2026-10-20T18:00:00Z")]
    [InlineData("2026-10-20T18:00:00.1234567-04:00")]
    public async Task ExplicitOffsetsRoundTripExactly(string timestamp)
    {
        var path = Path.Combine(directory, "offset.yaml");
        await File.WriteAllTextAsync(path, $"StartTime: {timestamp}\nMarker: []\n");
        var service = new LiveMarkerFileService();
        var document = await service.LoadAsync(path);
        var expected = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture);
        Assert.True(document.StartTime.EqualsExact(expected));

        await service.SaveAsync(document, path);
        Assert.Contains(expected.ToString("O", CultureInfo.InvariantCulture), await File.ReadAllTextAsync(path));
        Assert.True((await service.LoadAsync(path)).StartTime.EqualsExact(expected));
    }

    /// <summary>旧无 offset 日期按本机时区的墙钟时间解释，不从直播时间反算日期。</summary>
    [Fact]
    public async Task UnspecifiedLegacyDateUsesLocalWallTime()
    {
        var path = Path.Combine(directory, "local.yaml");
        await File.WriteAllTextAsync(path, """
            StartTime: 2026-10-20T18:00:00
            Marker:
            - RealDateTime: 2026-10-20T19:55:00
              LiveTime: 00:01:02
              Message: old
              MarkerColor: Grey
            """);
        var document = await new LiveMarkerFileService().LoadAsync(path);
        var expectedStart = new DateTimeOffset(new DateTime(2026, 10, 20, 18, 0, 0, DateTimeKind.Unspecified));
        var expectedReal = new DateTimeOffset(new DateTime(2026, 10, 20, 19, 55, 0, DateTimeKind.Unspecified));

        Assert.True(document.StartTime.EqualsExact(expectedStart));
        Assert.True(document.Marker[0].RealDateTime.EqualsExact(expectedReal));
        Assert.Equal(TimeSpan.FromSeconds(62), document.Marker[0].LiveTime);
    }

    /// <summary>支持旧 TimeSpan 天数格式及累计小时超过 24 的时长，精度保持到 tick。</summary>
    [Theory]
    [InlineData("1.01:02:03.1234567", 25, 2, 3, 1234567)]
    [InlineData("25:02:03.1234567", 25, 2, 3, 1234567)]
    [InlineData("30:00:00", 30, 0, 0, 0)]
    [InlineData("00:00:00", 0, 0, 0, 0)]
    public async Task DurationSupportsDaysAndUnboundedHours(string duration, int hours, int minutes, int seconds, int fraction)
    {
        var path = Path.Combine(directory, "duration.yaml");
        await File.WriteAllTextAsync(path, CreateYaml(duration));
        var service = new LiveMarkerFileService();
        var document = await service.LoadAsync(path);
        var expected = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) + TimeSpan.FromTicks(fraction);
        Assert.Equal(expected, document.Marker[0].LiveTime);

        await service.SaveAsync(document, path);
        Assert.Equal(expected, (await service.LoadAsync(path)).Marker[0].LiveTime);
    }

    /// <summary>缺失字段、非法字段和负时长都失败，不生成猜测时间或空文档。</summary>
    [Theory]
    [InlineData("Marker: []")]
    [InlineData("StartTime: 2026-10-20T18:00:00+08:00")]
    [InlineData("StartTime: invalid\nMarker: []")]
    [InlineData("StartTime: 2026-10-20T18:00:00+08:00\nMarker: {}")]
    [InlineData("StartTime: 2026-10-20T18:00:00+08:00\nMarker: []\nUnexpected: value")]
    [InlineData("StartTime: 2026-10-20T18:00:00+08:00\nMarker: [{ LiveTime: 00:01:00, Message: x, MarkerColor: Red }]")]
    public async Task InvalidStructureIsRejected(string yaml)
    {
        var path = Path.Combine(directory, "invalid.yaml");
        await File.WriteAllTextAsync(path, yaml);
        await Assert.ThrowsAsync<InvalidDataException>(() => new LiveMarkerFileService().LoadAsync(path));
    }

    /// <summary>非法时长、负时长和溢出时长均不可加载。</summary>
    [Theory]
    [InlineData("-00:00:01")]
    [InlineData("-1.01:00:00")]
    [InlineData("25:60:00")]
    [InlineData("25:00:60")]
    [InlineData("999999999999999999999:00:00")]
    [InlineData("invalid")]
    public async Task InvalidDurationIsRejected(string duration)
    {
        var path = Path.Combine(directory, "invalid-duration.yaml");
        await File.WriteAllTextAsync(path, CreateYaml(duration));
        await Assert.ThrowsAsync<InvalidDataException>(() => new LiveMarkerFileService().LoadAsync(path));
    }

    /// <summary>颜色仅接受旧枚举名称，不接受未知值或数字编号。</summary>
    [Theory]
    [InlineData("Unknown")]
    [InlineData("1")]
    [InlineData("-1")]
    public async Task InvalidColorIsRejected(string color)
    {
        var path = Path.Combine(directory, "invalid-color.yaml");
        await File.WriteAllTextAsync(path, CreateYaml("00:01:00").Replace("MarkerColor: Red", $"MarkerColor: {color}", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(() => new LiveMarkerFileService().LoadAsync(path));
    }

    /// <summary>取消保存不截断旧文件、不留下临时文件。</summary>
    [Fact]
    public async Task CanceledSaveKeepsExistingFile()
    {
        var path = Path.Combine(directory, "existing.yaml");
        await File.WriteAllTextAsync(path, "original bytes");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LiveMarkerFileService().SaveAsync(
            new() { StartTime = DateTimeOffset.Now }, path, cancellation.Token));

        Assert.Equal("original bytes", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    /// <summary>文档验证失败时旧文件保持原样。</summary>
    [Fact]
    public async Task InvalidSnapshotDoesNotOverwriteExistingFile()
    {
        var path = Path.Combine(directory, "existing.yaml");
        await File.WriteAllTextAsync(path, "original bytes");
        var invalid = new LiveMarkerDocument
        {
            StartTime = DateTimeOffset.Now,
            Marker = [new() { RealDateTime = DateTimeOffset.Now, LiveTime = TimeSpan.FromSeconds(-1) }]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => new LiveMarkerFileService().SaveAsync(invalid, path));
        Assert.Equal("original bytes", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    /// <summary>最终替换失败时保留已有目标目录及其内容，并清理已写完的临时文件。</summary>
    [Fact]
    public async Task FailedReplacementCleansTemporaryFileAndKeepsTarget()
    {
        var target = Path.Combine(directory, "target.yaml");
        Directory.CreateDirectory(target);
        var sentinel = Path.Combine(target, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "keep");

        await Assert.ThrowsAnyAsync<IOException>(() => new LiveMarkerFileService().SaveAsync(
            new() { StartTime = DateTimeOffset.Now }, target));

        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel));
        Assert.Empty(Directory.GetFiles(directory));
    }

    /// <summary>同一所有者重入租约必须逐个释放，重复释放同一租约是安全的。</summary>
    [Fact]
    public void LeaseRefCountKeepsOtherOwnersExcluded()
    {
        var path = Path.Combine(directory, "record.yaml");
        var service = new LiveMarkerFileService();
        var owner = new object();
        var first = service.Acquire(path, owner);
        var second = service.Acquire(Path.Combine(directory, ".", "record.yaml"), owner);
        Assert.Throws<IOException>(() => service.Acquire(path, new object()));
        first.Dispose();
        first.Dispose();
        Assert.Throws<IOException>(() => service.Acquire(path, new object()));
        second.Dispose();
        using var nextOwner = service.Acquire(path, new object());
    }

    /// <summary>目录符号链接和真实路径指向同一文件时不可分别取得写入租约。</summary>
    [Fact]
    public void DirectorySymbolicLinksCannotBypassLease()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var realDirectory = Path.Combine(directory, "real");
        Directory.CreateDirectory(realDirectory);
        var alias = Path.Combine(directory, "alias");
        Directory.CreateSymbolicLink(alias, realDirectory);
        var service = new LiveMarkerFileService();
        using var first = service.Acquire(Path.Combine(realDirectory, "record.yaml"), new object());
        Assert.Throws<IOException>(() => service.Acquire(Path.Combine(alias, "record.yaml"), new object()));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Directory.Delete(directory, true);
    }

    private static string CreateYaml(string duration)
    {
        return $"StartTime: 2026-10-20T18:00:00+08:00\nMarker:\n- RealDateTime: 2026-10-20T19:55:00+08:00\n  LiveTime: {duration}\n  Message: old\n  MarkerColor: Red\n";
    }
}
