using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用临时配置和离线发送服务验证歌词交互。
/// </summary>
public sealed class LyricNavigationViewTestContext : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 创建包含三句歌词的离线直播间。
    /// </summary>
    public LyricNavigationViewTestContext()
    {
        var stateService = new AppStateService(directory);
        stateService.Save(new()
        {
            Workspaces =
            [
                new()
                {
                    Name = "歌词交互测试",
                    LiveRooms =
                    [
                        new()
                        {
                            RoomId = "100", RoomName = "离线直播间",
                            LyricText = "[00:01]第一句\n[00:10]第二句\n[00:20]第三句"
                        }
                    ]
                }
            ]
        });
        var avatarService = new FakeRoomAvatarService();
        var moderationService = new FakeBilibiliModerationService();
        ViewModel = new(stateService, new FakeLiveRoomSearchService(), avatarService, moderationService);
        Room = ViewModel.Workspaces[0].Rooms[0];
        Room.Configure(
            _ => null,
            (_, _) => null,
            () => ViewModel.Settings,
            () => ViewModel.SymbolGroups[0],
            () => ViewModel.SymbolGroups,
            _ => [],
            SendService,
            avatarService,
            ViewModel.SaveAsync,
            moderationService: moderationService);
    }

    /// <summary>
    /// 主窗口视图模型。
    /// </summary>
    public MainWindowViewModel ViewModel { get; }

    /// <summary>
    /// 当前直播间。
    /// </summary>
    public LiveRoomTabViewModel Room { get; }

    /// <summary>
    /// 记录歌词发送内容的离线服务。
    /// </summary>
    public FakeDanmuSendService SendService { get; } = new();

    /// <inheritdoc/>
    public void Dispose()
    {
        ViewModel.Dispose();
        Directory.Delete(directory, true);
    }
}
