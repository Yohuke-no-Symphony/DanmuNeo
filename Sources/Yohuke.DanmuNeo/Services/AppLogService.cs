using Yohuke.DanmuNeo.Models.Logging;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 线程安全的应用会话日志，限制记录数量并统计未读错误。
/// </summary>
public sealed class AppLogService : IAppLogService
{
    private readonly object gate = new();
    private readonly Queue<AppLogEntry> entries = new();
    private readonly Dictionary<string, string> diagnostics = new(StringComparer.Ordinal);
    private readonly int capacity;
    private long sequence;
    private long readThrough;
    private bool disposed;

    /// <summary>创建指定容量的内存日志，默认保留最近 2000 条。</summary>
    public AppLogService(int capacity = 2000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.capacity = capacity;
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public IReadOnlyList<AppLogEntry> Entries
    {
        get
        {
            lock (gate)
            {
                return entries.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public int UnreadErrorCount
    {
        get
        {
            lock (gate)
            {
                return entries.Count(entry => entry.Sequence > readThrough && entry.Level == AppLogLevel.ERROR);
            }
        }
    }

    /// <inheritdoc/>
    public void Information(string source, string message, string? details = null)
    {
        Append(AppLogLevel.INFO, source, message, details);
    }

    /// <inheritdoc/>
    public void Warning(string source, string message, string? details = null)
    {
        Append(AppLogLevel.WARNING, source, message, details);
    }

    /// <inheritdoc/>
    public void Error(string source, string message, Exception? error = null)
    {
        Append(AppLogLevel.ERROR, source, message, error?.ToString());
    }

    /// <inheritdoc/>
    public void SetDiagnosticError(string source, Exception? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var details = error is null ? null : AppLogSanitizer.Sanitize(error.ToString());

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (details is null)
            {
                diagnostics.Remove(source);
                return;
            }

            if (diagnostics.TryGetValue(source, out var previous) && previous == details)
            {
                return;
            }

            diagnostics[source] = details;
            AppendCore(AppLogLevel.ERROR, source, error!.Message, details);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void MarkRead()
    {
        lock (gate)
        {
            if (disposed || readThrough == sequence)
            {
                return;
            }

            readThrough = sequence;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            entries.Clear();
            readThrough = sequence;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            diagnostics.Clear();
            Changed = null;
        }
    }

    private void Append(AppLogLevel level, string source, string message, string? details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(message);

        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            AppendCore(level, source, message, details);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AppendCore(AppLogLevel level, string source, string message, string? details)
    {
        entries.Enqueue(new(++sequence, DateTimeOffset.UtcNow, level,
            AppLogSanitizer.Sanitize(source), AppLogSanitizer.Sanitize(message),
            AppLogSanitizer.Sanitize(details ?? string.Empty)));

        while (entries.Count > capacity)
        {
            entries.Dequeue();
        }
    }
}
