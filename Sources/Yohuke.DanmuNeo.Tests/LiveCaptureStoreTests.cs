using Yohuke.DanmuNeo.Models.Captures;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证永久图库的原图、元数据、保存重试和时间语义。</summary>
public sealed class LiveCaptureStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "danmu-captures-" + Guid.NewGuid().ToString("N"));

    /// <summary>备注独立持久化且不改变永久原图字节。</summary>
    [Fact]
    public async Task NoteChangesPersistIndependentlyAndOriginalBytesNeverChange()
    {
        var store = new LiveCaptureStore(directory);
        var record = store.Add(CreateFrame(), "room name");
        await store.FlushAsync();
        var original = await File.ReadAllBytesAsync(store.GetOriginalPath(record.Id));
        store.UpdateNote(record.Id, "first\nsecond");
        await store.FlushAsync();
        Assert.Equal(original, await File.ReadAllBytesAsync(store.GetOriginalPath(record.Id)));
        Assert.Equal(Path.Combine(record.Id, "original.png").Replace('\\', '/'), record.OriginalRelativePath);
        var loaded = await new LiveCaptureStore(directory).LoadAsync();
        Assert.Equal("first\nsecond", Assert.Single(loaded).Note);
        Assert.Equal(record.StartedAt, loaded[0].StartedAt);
        Assert.Equal(record.CapturedAt, loaded[0].CapturedAt);
    }

    /// <summary>写入失败后保留原帧及最新备注供重试。</summary>
    [Fact]
    public async Task FailedFlushRetainsCaptureAndEditedNoteForRetry()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        await File.WriteAllTextAsync(directory, "block directory");
        var store = new LiveCaptureStore(directory);
        var record = store.Add(CreateFrame(), "room");
        await Assert.ThrowsAnyAsync<IOException>(() => store.FlushAsync());
        store.UpdateNote(record.Id, "saved after retry");
        Assert.True(store.HasPendingChanges);
        File.Delete(directory);
        await store.FlushAsync();
        Assert.False(store.HasPendingChanges);
        Assert.Equal("saved after retry", Assert.Single(await new LiveCaptureStore(directory).LoadAsync()).Note);
    }

    /// <summary>损坏或越界元数据不影响其他有效记录。</summary>
    [Fact]
    public async Task CorruptMetadataAndEscapingOriginalPathCannotHideValidRecords()
    {
        var store = new LiveCaptureStore(directory);
        store.Add(CreateFrame(), "valid");
        await store.FlushAsync();
        var corrupt = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(corrupt);
        await File.WriteAllTextAsync(Path.Combine(corrupt, "capture.json"), "{");
        var escapingId = Guid.NewGuid().ToString("N");
        var escaping = Path.Combine(directory, escapingId);
        Directory.CreateDirectory(escaping);
        var record = store.Records[0] with { Id = escapingId, OriginalRelativePath = "../outside.png" };
        await File.WriteAllTextAsync(Path.Combine(escaping, "capture.json"), System.Text.Json.JsonSerializer.Serialize(record));
        await File.WriteAllBytesAsync(Path.Combine(escaping, "original.png"), CreateFrame().PngBytes);
        var loadedStore = new LiveCaptureStore(directory);
        Assert.Single(await loadedStore.LoadAsync());
        Assert.Equal(2, loadedStore.LoadWarnings.Count);
    }

    /// <summary>调用方修改抓帧缓冲区不会改变已经交给永久图库的原图。</summary>
    [Fact]
    public async Task CallerBufferChangesCannotMutatePendingOriginal()
    {
        var store = new LiveCaptureStore(directory);
        var frame = CreateFrame();
        var expected = frame.PngBytes.ToArray();
        var record = store.Add(frame, "room");
        Array.Fill(frame.PngBytes, (byte)0);
        await store.FlushAsync();
        Assert.Equal(expected, await File.ReadAllBytesAsync(store.GetOriginalPath(record.Id)));
    }

    /// <summary>限制待写原图数量以约束磁盘故障时的内存。</summary>
    [Fact]
    public void PendingOriginalsHaveBoundedCapacity()
    {
        var store = new LiveCaptureStore(directory);
        var frame = CreateFrame();
        for (var index = 0; index < 4; index++)
        {
            store.Add(frame, "room");
        }
        Assert.Throws<InvalidOperationException>(() => store.Add(frame, "room"));
        Assert.Equal(4, store.Records.Count);
        Assert.True(store.HasPendingOriginals);
    }

    /// <summary>导出保留抓图时区偏移而不重新解释时间。</summary>
    [Fact]
    public void DisplayTimeRetainsCaptureOffsetWhenExportingLater()
    {
        var captured = new DateTimeOffset(2026, 10, 19, 12, 34, 56, TimeSpan.FromHours(-7));
        Assert.Contains("2026-10-19 12:34:56 -07:00", LiveCaptureTimeFormatter.FormatTime(captured));
    }

    /// <summary>时长按墙上时钟计算并保留跨日累计小时。</summary>
    [Fact]
    public void CaptureTimesUseWallClockAndKeepTotalHoursAcrossDates()
    {
        var start = new DateTimeOffset(2026, 10, 1, 23, 0, 0, TimeSpan.Zero);
        Assert.Equal("27:04:05", LiveCaptureTimeFormatter.FormatElapsed(start, start.AddHours(27).AddMinutes(4).AddSeconds(5)));
        Assert.Equal(Yohuke.DanmuNeo.I18n.Localization.Get("Captures.Unknown"), LiveCaptureTimeFormatter.FormatElapsed(null, start));
        Assert.Equal(Yohuke.DanmuNeo.I18n.Localization.Get("Captures.Unknown"), LiveCaptureTimeFormatter.FormatElapsed(start.AddHours(1), start));
    }

    internal static LiveCapturedFrame CreateFrame(string roomId = "offline", long liveTime = 1728000000)
    {
        return new()
        {
            PngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="),
            RoomId = roomId,
            BroadcastSessionId = roomId + ":" + liveTime,
            StartedAt = DateTimeOffset.FromUnixTimeSeconds(liveTime),
            CapturedAt = DateTimeOffset.FromUnixTimeSeconds(liveTime + 3600),
            Width = 1,
            Height = 1
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
        if (File.Exists(directory))
        {
            File.Delete(directory);
        }
    }
}
