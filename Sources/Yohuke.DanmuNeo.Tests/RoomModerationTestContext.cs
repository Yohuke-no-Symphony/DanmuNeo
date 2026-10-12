using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 不访问网络的房间禁言测试上下文。
/// </summary>
public sealed class RoomModerationTestContext : IDisposable
{
    /// <summary>
    /// 创建房间、登录账号和一条远端弹幕。
    /// </summary>
    public RoomModerationTestContext(ILiveDanmuSocketFactory? socketFactory = null,
        IBilibiliModerationService? moderationService = null)
    {
        Room = new(new() { RoomId = "100", OwnerUid = "999", RoomName = "测试直播间" });
        Room.Configure(
            _ => Account, (_, _) => Account, () => new(), () => new(), () => [], _ => [],
            new FakeDanmuSendService(), new FakeRoomAvatarService(), () => Task.CompletedTask,
            liveDanmuSocketFactory: socketFactory,
            moderationService: moderationService ?? Service);
        Room.IsSelected = true;
        Room.AddDanmu(new() { RoomId = "100", Uid = 30, UserName = "目标用户", Content = "远端弹幕" });
        Item = Room.DanmuItems.Single();
    }

    /// <summary>
    /// 当前解析的账号。
    /// </summary>
    public BilibiliAccount Account { get; set; } = new()
    {
        Id = "account",
        Cookie = "SESSDATA=test-session;bili_jct=test-csrf;DedeUserID=10"
    };

    /// <summary>
    /// 房间视图模型。
    /// </summary>
    public LiveRoomTabViewModel Room { get; }

    /// <summary>
    /// 目标弹幕。
    /// </summary>
    public DanmuFeedItem Item { get; }

    /// <summary>
    /// 离线服务。
    /// </summary>
    public FakeBilibiliModerationService Service { get; } = new();

    /// <inheritdoc/>
    public void Dispose()
    {
        Room.StopListening();
    }
}
