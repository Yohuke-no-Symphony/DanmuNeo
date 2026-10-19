using System.Threading;
using Yohuke.DanmuNeo.Models.Captures;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class ControlledLiveCaptureStore(string directory) : ILiveCaptureStore
{
    private readonly LiveCaptureStore store = new(directory);
    internal bool FailWrites { get; set; }
    internal int LoadCount { get; private set; }
    public IReadOnlyList<LiveCaptureRecord> Records => store.Records;
    public IReadOnlyList<string> LoadWarnings => store.LoadWarnings;
    public bool HasPendingChanges => store.HasPendingChanges;
    public bool HasPendingOriginals => store.HasPendingOriginals;

    /// <inheritdoc/>
    public Task<IReadOnlyList<LiveCaptureRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        LoadCount++;
        return store.LoadAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public LiveCaptureRecord Add(LiveCapturedFrame frame, string roomName) => store.Add(frame, roomName);
    /// <inheritdoc/>
    public void UpdateNote(string id, string note) => store.UpdateNote(id, note);
    /// <inheritdoc/>
    public string GetOriginalPath(string id) => store.GetOriginalPath(id);
    /// <inheritdoc/>
    public bool ContainsPath(string path) => store.ContainsPath(path);
    /// <inheritdoc/>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return FailWrites ? Task.FromException(new IOException("模拟图库写入失败")) : store.FlushAsync(cancellationToken);
    }
}
