using System.Threading;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class LiveMarkerSessionTestClock : ILiveMarkerClockService
{
    internal DateTimeOffset StartTime { get; set; } = new(2026, 10, 20, 11, 0, 0, TimeSpan.Zero);
    internal List<(string RoomId, string? Cookie)> Requests { get; } = [];
    internal Exception? Exception { get; set; }
    internal Func<CancellationToken, Task<DateTimeOffset>>? Response { get; set; }

    /// <inheritdoc/>
    public Task<DateTimeOffset> CreateStartTimeAsync(string roomId, string? cookie, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        Requests.Add((roomId, cookie));

        if (Exception is not null)
        {
            return Task.FromException<DateTimeOffset>(Exception);
        }

        return Response?.Invoke(cancellationToken) ?? Task.FromResult(StartTime);
    }
}
