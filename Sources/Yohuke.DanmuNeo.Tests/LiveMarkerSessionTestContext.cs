using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class LiveMarkerSessionTestContext : IDisposable
{
    internal LiveMarkerSessionTestContext(LiveMarkerSessionTestFiles? files = null,
        LiveMarkerSessionTestClock? clock = null, LiveMarkerTestTimeProvider? time = null,
        LiveRoomTabState? state = null, Func<Task>? saveState = null)
    {
        Files = files ?? new();
        Clock = clock ?? new();
        Time = time ?? new(new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        State = state ?? new() { RoomId = "100" };
        Services = new(Files, Clock, timeProvider: Time);
        ViewModel = new(State);
        ViewModel.Configure(Services, () => State.RoomId, () => "test-cookie", () => Settings,
            saveState ?? (() => Task.CompletedTask), null);
    }

    internal LiveMarkerSessionTestFiles Files { get; }
    internal LiveMarkerSessionTestClock Clock { get; }
    internal LiveMarkerTestTimeProvider Time { get; }
    internal LiveRoomTabState State { get; }
    internal AppSettings Settings { get; } = new() { LiveMarkerAutoSaveEnabled = false };
    internal LiveMarkerServices Services { get; }
    internal LiveMarkerViewModel ViewModel { get; }

    internal async Task<string> OpenAsync(LiveMarkerDocument? document = null)
    {
        var path = NewPath();
        Files.Documents[path] = document ?? new() { StartTime = Time.GetLocalNow() - TimeSpan.FromHours(1) };
        await ViewModel.OpenAsync(path);
        return path;
    }

    internal static string NewPath()
    {
        return Path.Combine(Path.GetTempPath(), "danmu-marker-session", Guid.NewGuid().ToString("N") + ".yaml");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ViewModel.Dispose();
        Services.Dispose();
    }
}
