namespace Yohuke.DanmuNeo.Services;

internal sealed class LiveMarkerFileLease(Action release) : IDisposable
{
    private Action? releaseAction = release;

    /// <inheritdoc/>
    public void Dispose()
    {
        Interlocked.Exchange(ref releaseAction, null)?.Invoke();
    }
}
