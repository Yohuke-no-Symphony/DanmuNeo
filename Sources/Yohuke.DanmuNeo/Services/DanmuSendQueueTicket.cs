namespace Yohuke.DanmuNeo.Services;

internal sealed class DanmuSendQueueTicket : IDisposable
{
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal DanmuSendQueueTicket(Task predecessor)
    {
        Ready = predecessor;
        Completion = CompleteAsync(predecessor, released.Task);
    }

    internal Task Ready { get; }
    internal Task Completion { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        released.TrySetResult();
    }

    private static async Task CompleteAsync(Task predecessor, Task release)
    {
        await predecessor.ConfigureAwait(false);
        await release.ConfigureAwait(false);
    }
}
