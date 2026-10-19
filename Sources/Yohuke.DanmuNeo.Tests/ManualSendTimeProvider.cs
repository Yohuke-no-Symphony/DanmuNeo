using System.Threading;
namespace Yohuke.DanmuNeo.Tests;

internal sealed class ManualSendTimeProvider : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ManualSendTimer> timers = [];
    private readonly SemaphoreSlim timerCreated = new(0);
    private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualSendTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (gate)
        {
            timers.Add(timer);
        }
        timerCreated.Release();
        return timer;
    }

    public async Task WaitForTimerAsync()
    {
        Assert.True(await timerCreated.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    public void Advance(TimeSpan elapsed)
    {
        List<ManualSendTimer> ready;
        lock (gate)
        {
            now += elapsed;
            ready = timers.Where(timer => timer.DueAt <= now && !timer.IsDisposed).ToList();
        }
        foreach (var timer in ready)
        {
            timer.Fire();
        }
    }
}
