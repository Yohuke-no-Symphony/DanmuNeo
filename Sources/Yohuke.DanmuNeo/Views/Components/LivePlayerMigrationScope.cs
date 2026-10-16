namespace Yohuke.DanmuNeo.Views.Components;

internal sealed class LivePlayerMigrationScope(Action complete) : IDisposable
{
    private Action? complete = complete;

    /// <inheritdoc/>
    public void Dispose()
    {
        Interlocked.Exchange(ref complete, null)?.Invoke();
    }
}
