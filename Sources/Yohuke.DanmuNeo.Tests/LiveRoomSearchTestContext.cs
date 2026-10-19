using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用临时配置和离线依赖的主播搜索测试上下文。
/// </summary>
public sealed class LiveRoomSearchTestContext : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 创建两个空工作区和可控制的搜索服务。
    /// </summary>
    public LiveRoomSearchTestContext(IBilibiliAccountSessionService? accountSessions = null)
    {
        StateService = new(directory);
        StateService.Save(new()
        {
            Accounts = [new() { Id = "account", Cookie = "buvid3=test-account", IsGlobalDefault = true }],
            Workspaces = [new() { Name = "目标工作区", SortOrder = 0 }, new() { Name = "其他工作区", SortOrder = 1 }]
        });
        ViewModel = new(StateService, SearchService, AvatarService,
            danmuConfigService: new FakeBilibiliDanmuConfigService(), accountSessions: accountSessions);
    }

    /// <summary>
    /// 主窗口视图模型。
    /// </summary>
    public MainWindowViewModel ViewModel { get; }

    /// <summary>
    /// 状态存储服务。
    /// </summary>
    public AppStateService StateService { get; }

    /// <summary>
    /// 搜索服务。
    /// </summary>
    public FakeLiveRoomSearchService SearchService { get; } = new();

    /// <summary>
    /// 头像服务。
    /// </summary>
    public FakeRoomAvatarService AvatarService { get; } = new();

    /// <summary>
    /// 创建搜索结果。
    /// </summary>
    public static LiveRoomSearchResult CreateResult(string roomId = "100")
    {
        return new() { RoomId = roomId, OwnerUid = "42", OwnerName = "主播昵称", Title = "直播标题" };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ViewModel.Dispose();
        Directory.Delete(directory, true);
    }
}
