using System.Collections.ObjectModel;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>不读写持久化账号的应用级禁言测试上下文。</summary>
public sealed class ModerationManagerTestContext : IDisposable
{
    /// <summary>创建两个账号及两个没有配置网络服务的房间。</summary>
    public ModerationManagerTestContext(bool hasDefault = true)
    {
        Accounts =
        [
            new() { Id = "first", Name = "第一账号", Cookie = "SESSDATA=first;bili_jct=first", IsGlobalDefault = hasDefault },
            new() { Id = "second", Name = "第二账号", Cookie = "SESSDATA=second;bili_jct=second" }
        ];
        Workspaces =
        [
            new(new()
            {
                AccountOverrideId = "second",
                LiveRooms =
                [
                    new() { RoomId = "100", RoomName = "房间甲", AccountOverrideId = "second" },
                    new() { RoomId = "200", RoomName = "房间乙" }
                ]
            }, _ => "账号")
        ];

        foreach (var account in Accounts)
        {
            Sessions.Register(account);
        }

        Service.PermissionResponseAsync = (roomId, _, _) => Task.FromResult(new RoomModerationPermission
        {
            RoomId = long.Parse(roomId), AccountUid = 10, OwnerUid = 20, IsRoomAdmin = true
        });
        Service.ListResponseAsync = (roomId, _, _) => Task.FromResult<IReadOnlyList<ModerationRecord>>
        ([
            new() { RecordId = roomId + 1, RoomId = roomId, UserUid = 30, UserName = "目标" }
        ]);
        Manager = new(Accounts, Workspaces, Service, Sessions, Log, () => TimeSpan.FromSeconds(2));
    }

    /// <summary>账号列表。</summary>
    public ObservableCollection<BilibiliAccount> Accounts { get; }

    /// <summary>跨工作区房间集合。</summary>
    public ObservableCollection<WorkspaceViewModel> Workspaces { get; }

    /// <summary>可控制的离线服务。</summary>
    public FakeBilibiliModerationService Service { get; } = new();

    /// <summary>共享账号有效性边界。</summary>
    public BilibiliAccountSessionService Sessions { get; } = new(new ControlledAccountValidationService());

    /// <summary>会话日志。</summary>
    public AppLogService Log { get; } = new();

    /// <summary>被测应用级模型。</summary>
    public ModerationManagerViewModel Manager { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        Manager.Dispose();
        Sessions.Dispose();
        Log.Dispose();

        foreach (var workspace in Workspaces)
        {
            workspace.Dispose();
        }
    }
}
