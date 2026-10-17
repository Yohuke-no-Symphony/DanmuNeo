using System.Diagnostics;
using Avalonia.Logging;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.Logging;

namespace Yohuke.DanmuNeo.Services;

internal sealed class AppLogRuntimeBridge : IDisposable
{
    private readonly IAppLogService log;
    private readonly AppLogTraceListener trace;
    private readonly ILogSink? previousSink;
    private readonly AppLogAvaloniaSink sink;
    private readonly object gate = new();
    private bool disposed;

    internal AppLogRuntimeBridge(IAppLogService log)
    {
        this.log = log;
        trace = new(log);
        previousSink = Logger.Sink;
        sink = new(log);
        Logger.Sink = sink;
        Trace.Listeners.Add(trace);
        StartupLog.Written += OnStartupLog;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Dispatcher.UIThread.UnhandledException += OnDispatcherException;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StartupLog.Written -= OnStartupLog;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            Dispatcher.UIThread.UnhandledException -= OnDispatcherException;
        }

        Trace.Listeners.Remove(trace);
        trace.Dispose();

        if (ReferenceEquals(Logger.Sink, sink))
        {
            Logger.Sink = previousSink;
        }
    }

    private void OnStartupLog(AppLogLevel level, string source, string message, Exception? error)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            switch (level)
            {
                case AppLogLevel.ERROR:
                    log.Error(source, message, error);
                    break;
                case AppLogLevel.WARNING:
                    log.Warning(source, message, error?.ToString());
                    break;
                default:
                    log.Information(source, message);
                    break;
            }
        }
    }

    private void RecordUnhandled(Exception error)
    {
        lock (gate)
        {
            if (!disposed)
            {
                log.SetDiagnosticError("未处理异常", error);
            }
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception error)
        {
            RecordUnhandled(error);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        RecordUnhandled(e.Exception);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        RecordUnhandled(e.Exception);
    }
}
