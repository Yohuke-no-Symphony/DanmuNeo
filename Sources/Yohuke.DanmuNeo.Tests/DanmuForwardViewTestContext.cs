using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用临时配置和离线连接验证转发页面生命周期。
/// </summary>
public sealed class DanmuForwardViewTestContext : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 创建有独立发送账号和符号组的转发规则。
    /// </summary>
    public DanmuForwardViewTestContext(FakeBilibiliUserNameService? userNameService = null)
    {
        UserNameService = userNameService ?? new();
        StateService = new(directory);
        StateService.Save(new()
        {
            Accounts =
            [
                new() { Id = "default-account", Name = "默认账号", Cookie = "default-cookie", IsGlobalDefault = true },
                new() { Id = "forward-account", Name = "转发账号", Cookie = "forward-cookie" }
            ],
            Settings = new()
            {
                MarkGroups =
                [
                    new() { Id = "default-group", Name = "默认符号", SortOrder = 0 },
                    new() { Id = "forward-group", Name = "转发符号", TranslateOpenMark = "[", TranslateCloseMark = "]", SortOrder = 1 }
                ]
            },
            Workspaces =
            [
                new()
                {
                    Id = "workspace", Name = "转发测试", SelectedLiveRoomId = "target-room",
                    LiveRooms =
                    [
                        new()
                        {
                            Id = "target-room", RoomId = "200", RoomName = "目标直播间",
                            ForwardRules =
                            [
                                new()
                                {
                                    SourceWorkspaceId = "workspace", SourceRoomStateId = "source-room", SenderUid = "42",
                                    AccountOverrideId = "forward-account", MarkSymbolGroupId = "forward-group"
                                }
                            ]
                        },
                        new() { Id = "source-room", RoomId = "100", RoomName = "来源直播间" }
                    ]
                }
            ]
        });
        var avatarService = new FakeRoomAvatarService();
        ViewModel = new(StateService, new FakeLiveRoomSearchService(), avatarService, new FakeBilibiliModerationService(),
            UserNameService);
        Room = ViewModel.Workspaces[0].Rooms.Single(room => room.Id == "target-room");
        Source = ViewModel.Workspaces[0].Rooms.Single(room => room.Id == "source-room");
        SocketFactory.Socket.KeepRunning = true;
        Room.Configure(
            _ => ViewModel.Accounts[0],
            (_, _) => ViewModel.Accounts[0],
            () => ViewModel.Settings,
            () => ViewModel.SymbolGroups[0],
            () => ViewModel.SymbolGroups,
            _ =>
            [
                new ForwardSourceRoomOption
                {
                    WorkspaceId = "workspace", WorkspaceName = "转发测试", RoomStateId = Source.Id,
                    RoomId = Source.RoomId, RoomName = Source.RoomName
                }
            ],
            SendService,
            avatarService,
            ViewModel.SaveAsync,
            resolveAccountById: accountId => ViewModel.Accounts.SingleOrDefault(account => account.Id == accountId),
            liveDanmuSocketFactory: SocketFactory,
            moderationService: new FakeBilibiliModerationService(),
            forwardAccountOptions: ViewModel.ForwardAccountOptions,
            userNameService: UserNameService);
        Room.RefreshForwardSourceRooms();
    }

    /// <summary>
    /// 临时状态服务。
    /// </summary>
    public AppStateService StateService { get; }

    /// <summary>
    /// 主窗口视图模型。
    /// </summary>
    public MainWindowViewModel ViewModel { get; }

    /// <summary>
    /// 接收转发的直播间。
    /// </summary>
    public LiveRoomTabViewModel Room { get; }

    /// <summary>
    /// 转发来源直播间。
    /// </summary>
    public LiveRoomTabViewModel Source { get; }

    /// <summary>
    /// 记录发送内容和账号的离线服务。
    /// </summary>
    public FakeDanmuSendService SendService { get; } = new();

    /// <summary>
    /// 可保持运行并主动推送弹幕的离线连接工厂。
    /// </summary>
    public FakeLiveDanmuSocketFactory SocketFactory { get; } = new();

    /// <summary>
    /// 离线用户名查询服务。
    /// </summary>
    public FakeBilibiliUserNameService UserNameService { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        ViewModel.Dispose();
        Directory.Delete(directory, true);
    }
}
