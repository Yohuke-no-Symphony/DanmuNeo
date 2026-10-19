using System.Threading;
namespace Yohuke.DanmuNeo.Tests;

internal sealed class ManualSendTimer(ManualSendTimeProvider clock, TimerCallback callback, object? state) : ITimer
{
    public DateTimeOffset DueAt { get; private set; } = DateTimeOffset.MaxValue;
    public bool IsDisposed { get; private set; }

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        DueAt = dueTime < TimeSpan.Zero ? DateTimeOffset.MaxValue : clock.GetUtcNow() + dueTime;
        return !IsDisposed;
    }

    public void Fire()
    {
        DueAt = DateTimeOffset.MaxValue;
        if (!IsDisposed)
        {
            callback(state);
        }
    }

    public void Dispose()
    {
        IsDisposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
