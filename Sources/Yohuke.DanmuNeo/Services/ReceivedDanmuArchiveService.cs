using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Services;

/// <summary>通过有界后台队列保存人类可读的 UTF-8 直播消息日志。</summary>
public sealed class ReceivedDanmuArchiveService : IReceivedDanmuArchiveService
{
    private const int DEFAULT_CAPACITY = 4096;
    private readonly object gate = new();
    private readonly AppDirectoryService directories;
    private readonly IAppLogService log;
    private readonly Channel<DanmuArchiveWriteRequest> queue;
    private readonly Func<string, TextWriter> writerFactory;
    private readonly Dictionary<string, (DateOnly Date, TextWriter Writer)> writers = [];
    private readonly Task worker;
    private bool disposed;

    /// <summary>创建归档器；注入的日志服务由调用方管理。</summary>
    public ReceivedDanmuArchiveService(AppDirectoryService directories, IAppLogService log,
        int capacity = DEFAULT_CAPACITY, Func<string, TextWriter>? writerFactory = null)
    {
        this.directories = directories;
        this.log = log;
        this.writerFactory = writerFactory ?? (path => new StreamWriter(path, true, new UTF8Encoding(false)));
        queue = Channel.CreateBounded<DanmuArchiveWriteRequest>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        worker = Task.Run(WriteLoopAsync);
    }

    /// <inheritdoc/>
    public DanmuArchiveSession OpenSession(string roomId, string workspaceId, string roomStateId)
    {
        if (!long.TryParse(roomId, out var numericId) || numericId <= 0)
        {
            throw new ArgumentException("Invalid public room ID.", nameof(roomId));
        }

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return new(numericId.ToString(CultureInfo.InvariantCulture), workspaceId, roomStateId);
        }
    }

    /// <inheritdoc/>
    public bool TryAppend(DanmuArchiveSession session, ReceivedDanmuLogEntry entry)
    {
        lock (gate)
        {
            if (disposed || session.IsClosed || session.HasFailed)
            {
                return false;
            }

            if (queue.Writer.TryWrite(new(session, entry)))
            {
                return true;
            }

            ReportFailure(session, new IOException(Localization.Get("Room.Archive.QueueFull")));
            return false;
        }
    }

    /// <inheritdoc/>
    public Task CompleteSessionAsync(DanmuArchiveSession session)
    {
        lock (gate)
        {
            if (disposed || session.IsClosed)
            {
                return worker.IsCompleted ? worker : Task.CompletedTask;
            }

            session.IsClosed = true;
            return EnqueueBarrierAsync(session);
        }
    }

    /// <inheritdoc/>
    public Task FlushAsync()
    {
        lock (gate)
        {
            return disposed ? worker : EnqueueBarrierAsync(null);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            disposed = true;
            queue.Writer.TryComplete();
        }

        await worker.ConfigureAwait(false);
    }

    private async Task EnqueueBarrierAsync(DanmuArchiveSession? session)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await queue.Writer.WriteAsync(new(session, null, completion)).ConfigureAwait(false);
            await completion.Task.ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            await worker.ConfigureAwait(false);
        }
    }

    private async Task WriteLoopAsync()
    {
        await foreach (var request in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (request.Session is { } session && request.Entry is { } entry)
                {
                    var date = DateOnly.FromDateTime(entry.ReceivedAt.LocalDateTime);
                    if (writers.TryGetValue(session.Id, out var existing) && existing.Date != date)
                    {
                        writers.Remove(session.Id);
                        await existing.Writer.DisposeAsync().ConfigureAwait(false);
                    }

                    if (!writers.TryGetValue(session.Id, out var current))
                    {
                        var folder = Path.Combine(directories.DanmuLogsDirectory, session.RoomId);
                        Directory.CreateDirectory(folder);
                        var path = Path.Combine(folder, $"{date:yyyy-MM-dd}-{session.Id}.log");
                        var writer = writerFactory(path);
                        writers[session.Id] = (date, writer);
                        await writer.WriteLineAsync($"# room={session.RoomId} workspace={Escape(session.WorkspaceId)} tab={Escape(session.RoomStateId)} session={session.Id}").ConfigureAwait(false);
                        current = (date, writer);
                    }

                    var serverTime = entry.ServerTimestamp > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(entry.ServerTimestamp).ToString("O", CultureInfo.InvariantCulture)
                        : "?";
                    var line = $"[{entry.ReceivedAt:O}] [{(entry.IsHistory ? "历史" : "实时")}] [{Escape(entry.Kind)}] [server={serverTime}] [UID={Escape(entry.UserUid)}] {Escape(entry.UserName)}: {Escape(entry.Content)}";
                    await current.Writer.WriteLineAsync(line).ConfigureAwait(false);
                    await current.Writer.FlushAsync().ConfigureAwait(false);
                }
                else if (request.Session is { } closedSession)
                {
                    if (writers.Remove(closedSession.Id, out var writer))
                    {
                        await writer.Writer.DisposeAsync().ConfigureAwait(false);
                    }
                }
                else
                {
                    foreach (var writer in writers.Values)
                    {
                        await writer.Writer.FlushAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exception)
            {
                if (request.Session is { } session)
                {
                    ReportFailure(session, exception);
                }
                else
                {
                    log.Error(Localization.Get("Room.Archive.Source"), Localization.Get("Room.Archive.Failed"), exception);
                }
            }
            finally
            {
                request.Completion?.TrySetResult();
            }
        }

        foreach (var writer in writers.Values)
        {
            try
            {
                await writer.Writer.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                log.Error(Localization.Get("Room.Archive.Source"), Localization.Get("Room.Archive.Failed"), exception);
            }
        }

        writers.Clear();
    }

    private void ReportFailure(DanmuArchiveSession session, Exception exception)
    {
        lock (gate)
        {
            if (session.HasFailed)
            {
                return;
            }

            session.HasFailed = true;
        }

        log.Error(Localization.Get("Room.Archive.Source"),
            Localization.Format("Room.Archive.RoomFailed", session.RoomId), exception);
    }

    private static string Escape(string text)
    {
        return text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
