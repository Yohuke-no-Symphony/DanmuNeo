namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkspaceLayoutSaveScheduler : IAsyncDisposable
{
    private static readonly TimeSpan SAVE_DELAY = TimeSpan.FromMilliseconds(300);
    private readonly object stateGate = new();
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly Func<WorkspaceLayoutFile, CancellationToken, Task> saveAsync;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
    private readonly List<Task> delayedSaves = [];
    private WorkspaceLayoutFile? pendingLayout;
    private long revision;
    private CancellationTokenSource? delayCancellation;
    private Task? disposal;
    private Exception? lastError;
    private bool isDisposing;
    private bool isDisposed;

    internal WorkspaceLayoutSaveScheduler(
        Func<WorkspaceLayoutFile, CancellationToken, Task> saveAsync,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(saveAsync);
        this.saveAsync = saveAsync;
        this.delayAsync = delayAsync ?? Task.Delay;
    }

    internal event Action<Exception>? SaveFailed;

    internal Exception? LastError
    {
        get
        {
            lock (stateGate)
            {
                return lastError;
            }
        }
    }

    internal void Schedule(WorkspaceLayoutFile file)
    {
        var snapshot = WorkspaceLayoutSerializer.Copy(file);

        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(isDisposing || isDisposed, this);
            pendingLayout = snapshot;
            revision++;
            delayCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            delayCancellation = cancellation;
            delayedSaves.RemoveAll(task => task.IsCompletedSuccessfully);
            delayedSaves.Add(SaveAfterDelayAsync(cancellation));
        }
    }

    internal Task FlushAsync(CancellationToken cancellationToken = default)
    {
        lock (stateGate)
        {
            delayCancellation?.Cancel();
        }

        return FlushCoreAsync(cancellationToken);
    }

    /// <summary>
    /// 刷新最后快照后停止调度；写入失败时恢复可用状态，允许更新快照后重试。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (stateGate)
        {
            if (disposal is not null)
            {
                return new(disposal);
            }

            isDisposing = true;
            delayCancellation?.Cancel();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disposal = completion.Task;
            _ = DisposeCoreAsync(delayedSaves.ToArray(), completion);
            return new(completion.Task);
        }
    }

    private async Task DisposeCoreAsync(Task[] pendingDelays, TaskCompletionSource completion)
    {
        try
        {
            await Task.WhenAll(pendingDelays).ConfigureAwait(false);
            await FlushCoreAsync(CancellationToken.None).ConfigureAwait(false);

            lock (stateGate)
            {
                isDisposing = false;
                isDisposed = true;
            }

            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            lock (stateGate)
            {
                isDisposing = false;
                disposal = null;
            }

            completion.TrySetException(exception);
        }
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource cancellation)
    {
        var waitingForDelay = true;

        try
        {
            await delayAsync(SAVE_DELAY, cancellation.Token).ConfigureAwait(false);
            waitingForDelay = false;
            cancellation.Token.ThrowIfCancellationRequested();
            await FlushCoreAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Action<Exception>? notify;

            lock (stateGate)
            {
                if (waitingForDelay && !isDisposed && !cancellation.IsCancellationRequested &&
                    ReferenceEquals(delayCancellation, cancellation))
                {
                    lastError = exception;
                }

                notify = !isDisposed && !cancellation.IsCancellationRequested && ReferenceEquals(lastError, exception)
                    ? SaveFailed
                    : null;
            }

            notify?.Invoke(exception);
        }
        finally
        {
            lock (stateGate)
            {
                if (ReferenceEquals(delayCancellation, cancellation))
                {
                    delayCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private async Task FlushCoreAsync(CancellationToken cancellationToken)
    {
        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            while (true)
            {
                WorkspaceLayoutFile file;
                long savingRevision;

                lock (stateGate)
                {
                    if (pendingLayout is null)
                    {
                        return;
                    }

                    file = pendingLayout;
                    savingRevision = revision;
                }

                try
                {
                    await saveAsync(file, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lock (stateGate)
                    {
                        if (savingRevision == revision)
                        {
                            lastError = exception;
                        }
                    }

                    throw;
                }

                lock (stateGate)
                {
                    lastError = null;

                    if (savingRevision == revision)
                    {
                        pendingLayout = null;
                    }
                }
            }
        }
        finally
        {
            saveGate.Release();
        }
    }
}
