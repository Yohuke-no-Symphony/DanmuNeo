using System.Text.Json;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Captures;

namespace Yohuke.DanmuNeo.Services;

/// <summary>独立永久图库。每条原图只写一次，元数据原子替换，失败保留待写状态。</summary>
public sealed class LiveCaptureStore(string directory) : ILiveCaptureStore
{
    private static readonly JsonSerializerOptions JSON_OPTIONS = new() { WriteIndented = true };
    private readonly string directory = Path.GetFullPath(directory);
    private readonly Dictionary<string, LiveCapturePendingRecord> records = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly List<string> loadWarnings = [];

    public IReadOnlyList<LiveCaptureRecord> Records
    {
        get
        {
            lock (gate)
            {
                return records.Values.Select(item => item.Record).OrderByDescending(item => item.CapturedAt).ToArray();
            }
        }
    }

    public IReadOnlyList<string> LoadWarnings
    {
        get
        {
            lock (gate)
            {
                return loadWarnings.ToArray();
            }
        }
    }

    public bool HasPendingOriginals
    {
        get
        {
            lock (gate)
            {
                return records.Values.Any(item => item.OriginalPng is not null);
            }
        }
    }

    public bool HasPendingChanges
    {
        get
        {
            lock (gate)
            {
                return records.Values.Any(item => item.Revision != item.SavedRevision);
            }
        }
    }

    /// <summary>读取各条独立元数据，损坏条目不阻止其他截图浏览。</summary>
    public async Task<IReadOnlyList<LiveCaptureRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await writer.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(directory))
            {
                return Records;
            }
            foreach (var recordDirectory in Directory.EnumerateDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = Path.GetFileName(recordDirectory);
                if (!Guid.TryParseExact(id, "N", out _))
                {
                    continue;
                }
                try
                {
                    var jsonPath = Path.Combine(recordDirectory, "capture.json");
                    if (new FileInfo(jsonPath).Length > 128 * 1024)
                    {
                        throw new InvalidDataException(Localization.Get("Captures.InvalidMetadata"));
                    }
                    var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
                    var record = JsonSerializer.Deserialize<LiveCaptureRecord>(json, JSON_OPTIONS);
                    if (record is null || record.Version != 1 || record.Id != id ||
                        record.OriginalRelativePath != $"{id}/original.png" ||
                        string.IsNullOrWhiteSpace(record.RoomId) || string.IsNullOrWhiteSpace(record.BroadcastSessionId) ||
                        record.CapturedAt == default || record.Width <= 0 || record.Height <= 0 || record.Note is null ||
                        record.Note.Length > 10000 || !File.Exists(Path.Combine(recordDirectory, "original.png")))
                    {
                        throw new InvalidDataException(Localization.Get("Captures.InvalidMetadata"));
                    }
                    lock (gate)
                    {
                        records.TryAdd(id, new() { Record = record, SavedRevision = 1 });
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
                {
                    lock (gate)
                    {
                        if (!loadWarnings.Contains(id))
                        {
                            loadWarnings.Add(id);
                        }
                    }
                }
            }
            return Records;
        }
        finally
        {
            writer.Release();
        }
    }

    /// <summary>保留新截图及原始字节，后续落盘失败仍可重试。</summary>
    public LiveCaptureRecord Add(LiveCapturedFrame frame, string roomName)
    {
        var size = LiveCapturePngValidator.Validate(frame.PngBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(frame.RoomId);
        ArgumentException.ThrowIfNullOrWhiteSpace(frame.BroadcastSessionId);
        if (size.Width != frame.Width || size.Height != frame.Height || frame.CapturedAt == default)
        {
            throw new InvalidDataException(Localization.Get("Captures.InvalidImage"));
        }
        var id = Guid.NewGuid().ToString("N");
        var record = new LiveCaptureRecord
        {
            Id = id,
            RoomId = frame.RoomId,
            RoomName = roomName ?? "",
            BroadcastSessionId = frame.BroadcastSessionId,
            StartedAt = frame.StartedAt,
            CapturedAt = frame.CapturedAt,
            Width = frame.Width,
            Height = frame.Height,
            OriginalRelativePath = $"{id}/original.png"
        };
        lock (gate)
        {
            if (records.Values.Count(item => item.OriginalPng is not null) >= 4 ||
                records.Values.Sum(item => (long)(item.OriginalPng?.Length ?? 0)) + frame.PngBytes.Length > 64 * 1024 * 1024)
            {
                throw new InvalidOperationException(Localization.Get("Captures.SaveBeforeCapture"));
            }
            records.Add(id, new() { Record = record, OriginalPng = frame.PngBytes.ToArray() });
        }
        return record;
    }

    /// <summary>只更新备注，保留不可变原图与抓图时记录的房间、场次和时间。</summary>
    public void UpdateNote(string id, string note)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(note.Length, 10000);
        lock (gate)
        {
            var pending = records[id];
            if (pending.Record.Note == note)
            {
                return;
            }
            pending.Record = pending.Record with { Note = note };
            pending.Revision++;
        }
    }

    /// <summary>获得已知条目的原图路径，拒绝从元数据注入其他路径。</summary>
    public string GetOriginalPath(string id)
    {
        lock (gate)
        {
            _ = records[id];
        }
        return Path.Combine(directory, id, "original.png");
    }

    /// <summary>保存全部待写内容；失败或取消保留原始数据和最新备注。</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await writer.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                LiveCaptureRecord? snapshot;
                byte[]? png;
                long revision;
                lock (gate)
                {
                    var pending = records.Values.FirstOrDefault(item => item.Revision != item.SavedRevision);
                    if (pending is null)
                    {
                        return;
                    }
                    snapshot = pending.Record;
                    png = pending.OriginalPng;
                    revision = pending.Revision;
                }
                cancellationToken.ThrowIfCancellationRequested();
                var originalPath = GetOriginalPath(snapshot.Id);
                Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
                if (png is not null)
                {
                    if (!File.Exists(originalPath))
                    {
                        await AtomicWriteAsync(originalPath, png, false, cancellationToken);
                    }
                    else if (!(await File.ReadAllBytesAsync(originalPath, cancellationToken)).AsSpan().SequenceEqual(png))
                    {
                        throw new InvalidDataException(Localization.Get("Captures.OriginalConflict"));
                    }
                }
                var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, JSON_OPTIONS);
                await AtomicWriteAsync(Path.Combine(directory, snapshot.Id, "capture.json"), json, true,
                    cancellationToken);
                lock (gate)
                {
                    var pending = records[snapshot.Id];
                    pending.OriginalPng = null;
                    pending.SavedRevision = revision;
                }
            }
        }
        finally
        {
            writer.Release();
        }
    }

    /// <inheritdoc/>
    public bool ContainsPath(string path)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = ResolvePath(directory).TrimEnd(Path.DirectorySeparatorChar);
        var candidate = ResolvePath(path).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(root, candidate, comparison) ||
            candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static string ResolvePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var resolved = root;
        foreach (var segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Combine(resolved, segment);
            if (Directory.Exists(resolved))
            {
                resolved = new DirectoryInfo(resolved).ResolveLinkTarget(true)?.FullName ?? resolved;
            }
            else if (File.Exists(resolved))
            {
                resolved = new FileInfo(resolved).ResolveLinkTarget(true)?.FullName ?? resolved;
            }
        }
        return Path.GetFullPath(resolved);
    }

    private static async Task AtomicWriteAsync(string path, byte[] bytes, bool overwrite,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
