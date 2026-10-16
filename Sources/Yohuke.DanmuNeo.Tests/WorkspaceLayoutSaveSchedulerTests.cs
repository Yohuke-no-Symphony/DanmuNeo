using System.Threading;
using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证布局保存的防抖、串行提交和关闭刷新。
/// </summary>
public class WorkspaceLayoutSaveSchedulerTests
{
    private readonly Queue<TaskCompletionSource> delays = new();
    private readonly List<TimeSpan> requestedDelays = [];

    /// <summary>
    /// 每次调度重新等待 300ms，过期计时器不会提交旧布局。
    /// </summary>
    [Fact]
    public async Task DebounceSavesOnlyLatestSnapshot()
    {
        var saved = new List<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new WorkspaceLayoutSaveScheduler((file, _) =>
        {
            saved.Add(file.CurrentPresetId);
            completed.TrySetResult();
            return Task.CompletedTask;
        }, DelayAsync);

        scheduler.Schedule(CreateFile("first"));
        scheduler.Schedule(CreateFile("second"));
        Assert.Empty(saved);
        Assert.Equal([TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300)], requestedDelays);
        delays.Dequeue().TrySetResult();
        delays.Dequeue().TrySetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scheduler.FlushAsync();

        Assert.Equal(["second"], saved);
    }

    /// <summary>
    /// Flush 立即提交调度时的独立快照，重复刷新不重复写盘。
    /// </summary>
    [Fact]
    public async Task FlushCapturesScheduledValueAndDoesNotDuplicateWrites()
    {
        var saved = new List<WorkspaceLayoutFile>();
        await using var scheduler = new WorkspaceLayoutSaveScheduler((file, _) =>
        {
            saved.Add(file);
            return Task.CompletedTask;
        }, DelayAsync);
        var file = CreateFile("snapshot");
        scheduler.Schedule(file);
        file.UserPresets.Clear();
        file.CurrentLayout.Main!.Children.Clear();

        await scheduler.FlushAsync();
        await scheduler.FlushAsync();

        var actual = Assert.Single(saved);
        Assert.Single(actual.UserPresets);
        Assert.True(WorkspaceLayoutValidator.TryValidate(actual, out var error), error);
        Assert.NotSame(file, actual);
    }

    /// <summary>
    /// 在途保存期间的新调度按代次合并，Flush 等待最后布局完成。
    /// </summary>
    [Fact]
    public async Task InFlightWriteCannotOverwriteLaterLayout()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new List<string>();
        await using var scheduler = new WorkspaceLayoutSaveScheduler(async (file, _) =>
        {
            if (file.CurrentPresetId == "first")
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task;
            }

            saved.Add(file.CurrentPresetId);
        }, DelayAsync);
        scheduler.Schedule(CreateFile("first"));
        var flushing = scheduler.FlushAsync();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            scheduler.Schedule(CreateFile("skipped"));
            scheduler.Schedule(CreateFile("last"));
            Assert.False(flushing.IsCompleted);
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        await flushing.WaitAsync(TimeSpan.FromSeconds(5));
        await scheduler.FlushAsync();

        Assert.Equal(["first", "last"], saved);
    }

    /// <summary>
    /// 保存失败保留待保存布局，错误可观察，之后可重试成功。
    /// </summary>
    [Fact]
    public async Task FailedWriteRemainsPendingForRetry()
    {
        var shouldFail = true;
        var saved = new List<string>();
        await using var scheduler = new WorkspaceLayoutSaveScheduler((file, _) =>
        {
            if (shouldFail)
            {
                return Task.FromException(new IOException("测试写入失败"));
            }

            saved.Add(file.CurrentPresetId);
            return Task.CompletedTask;
        }, DelayAsync);
        scheduler.Schedule(CreateFile("retry"));

        await Assert.ThrowsAsync<IOException>(() => scheduler.FlushAsync());
        Assert.IsType<IOException>(scheduler.LastError);
        shouldFail = false;
        await scheduler.FlushAsync();

        Assert.Equal(["retry"], saved);
        Assert.Null(scheduler.LastError);
    }

    /// <summary>
    /// 定时保存失败通知调用方，下一次调度仍可正常保存。
    /// </summary>
    [Fact]
    public async Task BackgroundFailureIsObservedAndNewScheduleCanRecover()
    {
        var shouldFail = true;
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new List<string>();
        await using var scheduler = new WorkspaceLayoutSaveScheduler((file, _) =>
        {
            if (shouldFail)
            {
                throw new IOException("测试后台写入失败");
            }

            saved.Add(file.CurrentPresetId);
            return Task.CompletedTask;
        }, DelayAsync);
        scheduler.SaveFailed += exception => failed.TrySetResult(exception);
        scheduler.Schedule(CreateFile("failed"));
        delays.Dequeue().TrySetResult();

        Assert.IsType<IOException>(await failed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        shouldFail = false;
        scheduler.Schedule(CreateFile("recovered"));
        await scheduler.FlushAsync();

        Assert.Equal(["recovered"], saved);
        Assert.Null(scheduler.LastError);
    }

    /// <summary>
    /// 旧后台保存迟到失败时，新布局刷新仍能完成且不会收到过期错误。
    /// </summary>
    [Fact]
    public async Task SupersededFailureCannotReplaceSuccessfulFlushState()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var saved = new List<string>();
        await using var scheduler = new WorkspaceLayoutSaveScheduler(async (file, _) =>
        {
            if (file.CurrentPresetId == "old")
            {
                firstStarted.TrySetResult();
                await firstResult.Task;
            }

            saved.Add(file.CurrentPresetId);
        }, DelayAsync);
        scheduler.SaveFailed += failures.Enqueue;
        scheduler.Schedule(CreateFile("old"));
        delays.Dequeue().TrySetResult();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scheduler.Schedule(CreateFile("current"));
        var flushing = scheduler.FlushAsync();
        firstResult.TrySetException(new IOException("旧布局迟到失败"));

        await flushing.WaitAsync(TimeSpan.FromSeconds(5));
        await scheduler.DisposeAsync();

        Assert.Equal(["current"], saved);
        Assert.Null(scheduler.LastError);
        Assert.Empty(failures);
    }

    /// <summary>
    /// 释放等待已经取消但尚未结束的计时任务，过期计时错误不覆盖已保存状态。
    /// </summary>
    [Fact]
    public async Task DisposalWaitsForSupersededDelayAndIgnoresItsFailure()
    {
        var oldDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCount = 0;
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        await using var scheduler = new WorkspaceLayoutSaveScheduler((_, _) => Task.CompletedTask,
            (_, token) => ++delayCount == 1 ? oldDelay.Task : Task.Delay(Timeout.Infinite, token));
        scheduler.SaveFailed += failures.Enqueue;
        scheduler.Schedule(CreateFile("old"));
        scheduler.Schedule(CreateFile("current"));
        await scheduler.FlushAsync();
        var disposing = scheduler.DisposeAsync().AsTask();

        try
        {
            Assert.False(disposing.IsCompleted);
        }
        finally
        {
            oldDelay.TrySetException(new IOException("过期计时任务失败"));
        }

        await disposing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(scheduler.LastError);
        Assert.Empty(failures);
    }

    /// <summary>
    /// 取消刷新不丢失待保存内容，后续刷新可以提交。
    /// </summary>
    [Fact]
    public async Task CanceledFlushKeepsPendingSnapshot()
    {
        var saved = new List<string>();
        await using var scheduler = new WorkspaceLayoutSaveScheduler((file, _) =>
        {
            saved.Add(file.CurrentPresetId);
            return Task.CompletedTask;
        }, DelayAsync);
        scheduler.Schedule(CreateFile("pending"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scheduler.FlushAsync(cancellation.Token));
        Assert.Empty(saved);
        await scheduler.FlushAsync();

        Assert.Equal(["pending"], saved);
    }

    /// <summary>
    /// 异步释放等待最后保存，重复释放安全，释放后拒绝新调度。
    /// </summary>
    [Fact]
    public async Task DisposalFlushesOnceAndRejectsFurtherScheduling()
    {
        var saved = new List<string>();
        var scheduler = new WorkspaceLayoutSaveScheduler((file, _) =>
        {
            saved.Add(file.CurrentPresetId);
            return Task.CompletedTask;
        }, DelayAsync);
        scheduler.Schedule(CreateFile("closing"));

        await scheduler.DisposeAsync();
        await scheduler.DisposeAsync();

        Assert.Equal(["closing"], saved);
        Assert.Throws<ObjectDisposedException>(() => scheduler.Schedule(CreateFile("too-late")));
    }

    private Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        requestedDelays.Add(duration);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        delays.Enqueue(completion);
        return completion.Task.WaitAsync(cancellationToken);
    }

    private static WorkspaceLayoutFile CreateFile(string id)
    {
        var file = WorkspaceLayoutPresets.CreateDefaultFile();
        return file with
        {
            CurrentPresetId = id,
            UserPresets = [new() { Id = id, Name = id, Layout = file.CurrentLayout }]
        };
    }
}
