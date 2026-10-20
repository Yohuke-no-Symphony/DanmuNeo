using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class LiveMarkerApplicationTestContext : IAsyncDisposable
{
    internal LiveMarkerApplicationTestContext()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "danmu-marker-application-" + Guid.NewGuid().ToString("N"));
        StateService = new(DirectoryPath);
        StateService.Save(new()
        {
            Settings = new()
            {
                LiveMarkerAutoSaveEnabled = false,
                CheckForUpdatesOnStartup = false,
                ArchiveReceivedDanmu = false
            },
            SelectedWorkspaceId = "workspace-a",
            Workspaces =
            [
                new()
                {
                    Id = "workspace-a", Name = "当前工作区", SortOrder = 0,
                    SelectedLiveRoomId = "room-a",
                    LiveRooms =
                    [
                        new() { Id = "room-a", RoomId = "100", RoomName = "第一个房间", SortOrder = 0 },
                        new() { Id = "room-b", RoomId = "200", RoomName = "第二个房间", SortOrder = 1 }
                    ]
                },
                new()
                {
                    Id = "workspace-b", Name = "保留工作区", SortOrder = 1,
                    LiveRooms = [new() { Id = "room-c", RoomId = "300", RoomName = "保留房间" }]
                }
            ]
        });
        Services = new(Files, Clock, timeProvider: Time);
        Application = new(StateService, new FakeLiveRoomSearchService(), new FakeRoomAvatarService(),
            moderationService: new FakeBilibiliModerationService(),
            userNameService: new FakeBilibiliUserNameService(),
            danmuConfigService: new FakeBilibiliDanmuConfigService(),
            liveMarkerServices: Services);
    }

    internal string DirectoryPath { get; }
    internal AppStateService StateService { get; }
    internal LiveMarkerSessionTestFiles Files { get; } = new();
    internal LiveMarkerSessionTestClock Clock { get; } = new();
    internal LiveMarkerTestTimeProvider Time { get; } = new(new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    internal LiveMarkerServices Services { get; }
    internal MainWindowViewModel Application { get; }
    internal WorkspaceViewModel Workspace => Application.Workspaces.Single(workspace => workspace.Id == "workspace-a");
    internal LiveRoomTabViewModel FirstRoom => Workspace.Rooms.Single(room => room.Id == "room-a");
    internal LiveRoomTabViewModel SecondRoom => Workspace.Rooms.Single(room => room.Id == "room-b");

    internal async Task<string> OpenDirtyAsync(LiveRoomTabViewModel room, string message)
    {
        var path = Path.Combine(DirectoryPath, room.Id + ".yaml");
        Files.Documents[path] = new() { StartTime = Time.GetLocalNow() - TimeSpan.FromHours(1) };
        await room.LiveMarker.OpenAsync(path);
        room.LiveMarker.DraftText = message;
        room.LiveMarker.AddMarkerCommand.Execute(null);
        Assert.True(room.LiveMarker.IsDirty);
        Assert.Single(room.LiveMarker.Entries);
        return path;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await Application.DisposeAsync();
        }
        finally
        {
            Services.Dispose();
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
