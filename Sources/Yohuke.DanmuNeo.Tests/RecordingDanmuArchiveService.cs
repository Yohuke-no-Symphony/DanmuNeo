using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class RecordingDanmuArchiveService : IReceivedDanmuArchiveService
{
    internal List<DanmuArchiveSession> Sessions { get; } = [];
    internal List<ReceivedDanmuLogEntry> Entries { get; } = [];

    /// <inheritdoc/>
    public DanmuArchiveSession OpenSession(string roomId, string workspaceId, string roomStateId)
    {
        var session = new DanmuArchiveSession(roomId, workspaceId, roomStateId);
        Sessions.Add(session);
        return session;
    }

    /// <inheritdoc/>
    public bool TryAppend(DanmuArchiveSession session, ReceivedDanmuLogEntry entry)
    {
        Entries.Add(entry);
        return true;
    }

    /// <inheritdoc/>
    public Task CompleteSessionAsync(DanmuArchiveSession session)
    {
        session.IsClosed = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FlushAsync() => Task.CompletedTask;
    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
