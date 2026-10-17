using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Yohuke.DanmuNeo.Services;

internal sealed class AppLogTraceListener(IAppLogService log) : TraceListener
{
    private const int MAXIMUM_MESSAGE_LENGTH = 8192;
    private readonly object gate = new();
    private readonly StringBuilder pending = new();
    private bool disposed;

    /// <inheritdoc/>
    public override bool IsThreadSafe => true;

    /// <inheritdoc/>
    public override void Write(string? message)
    {
        lock (gate)
        {
            if (!disposed && message is not null)
            {
                pending.Append(message.AsSpan(0, Math.Min(message.Length, MAXIMUM_MESSAGE_LENGTH - pending.Length)));
            }
        }
    }

    /// <inheritdoc/>
    public override void WriteLine(string? message)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            Write(message);
            var text = pending.ToString();
            pending.Clear();

            if (!string.IsNullOrWhiteSpace(text))
            {
                log.Information("Trace", text);
            }
        }
    }

    /// <inheritdoc/>
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType,
        int id, string? message)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (eventType is TraceEventType.Critical or TraceEventType.Error)
            {
                log.Error(source, message ?? "");
            }
            else if (eventType == TraceEventType.Warning)
            {
                log.Warning(source, message ?? "");
            }
            else
            {
                log.Information(source, message ?? "");
            }
        }
    }

    /// <inheritdoc/>
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType,
        int id, string? format, params object?[]? args)
    {
        var message = args is { Length: > 0 }
            ? string.Format(CultureInfo.InvariantCulture, format ?? "", args)
            : format;
        TraceEvent(eventCache, source, eventType, id, message);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        lock (gate)
        {
            disposed = true;
            pending.Clear();
        }

        base.Dispose(disposing);
    }
}
