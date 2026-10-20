using System.Threading;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class LiveMarkerSessionTestFiles : ILiveMarkerFileService
{
    private readonly LiveMarkerFileService leases = new();

    internal Dictionary<string, LiveMarkerDocument> Documents { get; } = [];
    internal List<string> Loads { get; } = [];
    internal List<(string Path, LiveMarkerDocument Snapshot)> Saves { get; } = [];
    internal Exception? LoadException { get; set; }
    internal Exception? SaveException { get; set; }
    internal Func<string, CancellationToken, Task<LiveMarkerDocument>>? LoadOverride { get; set; }
    internal Func<LiveMarkerDocument, string, CancellationToken, Task>? BeforeSave { get; set; }

    /// <inheritdoc/>
    public IDisposable Acquire(string filePath, object owner)
    {
        return leases.Acquire(filePath, owner);
    }

    /// <inheritdoc/>
    public async Task<LiveMarkerDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Loads.Add(filePath);

        if (LoadException is not null)
        {
            throw LoadException;
        }

        var document = LoadOverride is not null
            ? await LoadOverride(filePath, cancellationToken)
            : Documents[filePath];
        return document with { Marker = document.Marker.ToArray() };
    }

    /// <inheritdoc/>
    public async Task SaveAsync(LiveMarkerDocument document, string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = document with { Marker = document.Marker.ToArray() };
        Saves.Add((filePath, snapshot));

        if (SaveException is not null)
        {
            throw SaveException;
        }

        if (BeforeSave is not null)
        {
            await BeforeSave(snapshot, filePath, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Documents[filePath] = snapshot;
    }
}
