using System.Threading;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class LiveMarkerUncancelableTestFiles(LiveMarkerSessionTestFiles inner) : ILiveMarkerFileService
{
    /// <inheritdoc/>
    public IDisposable Acquire(string filePath, object owner)
    {
        return inner.Acquire(filePath, owner);
    }

    /// <inheritdoc/>
    public Task<LiveMarkerDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return inner.LoadAsync(filePath, cancellationToken);
    }

    /// <inheritdoc/>
    public Task SaveAsync(LiveMarkerDocument document, string filePath, CancellationToken cancellationToken = default)
    {
        return inner.SaveAsync(document, filePath, CancellationToken.None);
    }
}
