using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class CaptureWorkbenchTestContext : IDisposable
{
    private readonly BilibiliAccountSessionService sessions = new(new MedalValidationService());

    internal CaptureWorkbenchTestContext(FakeLiveFrameCaptureService? frames = null,
        ILiveCaptureExportService? exporter = null)
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "danmu-workbench-captures-" + Guid.NewGuid().ToString("N"));
        Store = new(Path.Combine(DirectoryPath, "gallery"));
        Frames = frames ?? new(LiveCaptureStoreTests.CreateFrame("123456"));
        var state = new AppState
        {
            Accounts = [new() { Id = "offline", Name = "离线测试账号", Cookie = "SESSDATA=offline;bili_jct=csrf;DedeUserID=42", IsGlobalDefault = true }],
            Workspaces = [new() { Name = "离线工作区", LiveRooms = [new() { RoomId = "6", RoomName = "本地房间名称" }] }]
        };
        Application = new(new(DirectoryPath), avatarCacheService: new FakeRoomAvatarService(),
            moderationService: Moderation, danmuConfigService: new FakeBilibiliDanmuConfigService(),
            loadedState: state, accountSessions: sessions, medalService: new FakeMedalService(),
            liveCaptureStore: Store, liveCaptureExportService: exporter, frameCaptureService: Frames);
    }

    internal string DirectoryPath { get; }
    internal ControlledLiveCaptureStore Store { get; }
    internal FakeLiveFrameCaptureService Frames { get; }
    internal FakeBilibiliModerationService Moderation { get; } = new();
    internal MainWindowViewModel Application { get; }
    internal LiveRoomTabViewModel Room => Application.Workspaces[0].Rooms[0];

    internal string EnablePlayer()
    {
        var token = Guid.NewGuid().ToString("N");
        Room.LivePlayerUrl = "http://127.0.0.1:1/player?token=" + token;
        Room.IsLivePlayerVisible = true;
        return token;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Application.Dispose();
        sessions.Dispose();
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, true);
        }
    }
}
