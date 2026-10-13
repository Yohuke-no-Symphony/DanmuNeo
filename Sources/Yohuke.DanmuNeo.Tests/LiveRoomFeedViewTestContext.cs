using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用真实工作区绑定和离线 API 的弹幕界面测试上下文。
/// </summary>
public sealed class LiveRoomFeedViewTestContext : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 创建含登录账号和单个直播间的工作区。
    /// </summary>
    public LiveRoomFeedViewTestContext()
    {
        var stateService = new AppStateService(directory);
        stateService.Save(new()
        {
            Accounts =
            [
                new()
                {
                    Id = "account", Name = "测试房管", Cookie = "SESSDATA=test;bili_jct=test;DedeUserID=10",
                    IsGlobalDefault = true
                }
            ],
            Workspaces = [new() { Name = "工作区", LiveRooms = [new() { RoomId = "100", RoomName = "测试直播间" }] }]
        });
        ViewModel = new(stateService, new FakeLiveRoomSearchService(), new FakeRoomAvatarService(), ModerationService,
            new FakeBilibiliUserNameService(), DanmuConfigService);
        Room = ViewModel.Workspaces[0].Rooms[0];
        Room.AddDanmu(new()
            { RoomId = "100", Uid = 30, UserName = "带牌用户", Content = "粉丝牌弹幕", MedalName = "夜更", MedalLevel = 25 });
        Room.AddDanmu(new() { RoomId = "100", Uid = 31, UserName = "无牌用户", Content = "普通弹幕" });
        Room.AddSuperChat(new()
        {
            RoomId = "100", MessageId = "sc1", UserName = "SC用户", PriceText = "¥30", Content = "感谢主播",
            Timestamp = 1800000000
        });
        Room.AddGuardPurchase(new()
        {
            RoomId = "100", MessageId = "guard1", Uid = 42, UserName = "上舰用户", GuardLevel = 3, Timestamp = 1800000001,
            Quantity = 1
        });
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
    /// 可控制权限响应的离线服务。
    /// </summary>
    public FakeBilibiliModerationService ModerationService { get; } = new();

    /// <summary>
    /// 可控制弹幕颜色和位置的离线服务。
    /// </summary>
    public FakeBilibiliDanmuConfigService DanmuConfigService { get; } = new();

    /// <inheritdoc/>
    public void Dispose()
    {
        ViewModel.Dispose();
        Directory.Delete(directory, true);
    }
}
