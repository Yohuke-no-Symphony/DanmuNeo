using System.Text.Json;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用离线依赖和临时配置验证侧栏布局。
/// </summary>
public sealed class WorkspaceSidebarTestContext : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 创建具有指定侧栏宽度和折叠状态的测试上下文。
    /// </summary>
    public WorkspaceSidebarTestContext(double sidebarWidth, bool isSidebarCollapsed = false)
    {
        StateService = new(directory);
        StateService.Save(new()
        {
            Workspaces =
            [
                new()
                {
                    Name = "当前工作区",
                    LiveRooms = [new() { RoomId = "100", RoomName = "离线房间" }]
                }
            ]
        });
        File.WriteAllText(StateService.SettingsFilePath, JsonSerializer.Serialize(new
        {
            SidebarWidth = sidebarWidth,
            IsSidebarCollapsed = isSidebarCollapsed
        }));
        ViewModel = new(StateService, new FakeLiveRoomSearchService(), new FakeRoomAvatarService());
    }

    /// <summary>
    /// 临时状态服务。
    /// </summary>
    public AppStateService StateService { get; }

    /// <summary>
    /// 主窗口视图模型。
    /// </summary>
    public MainWindowViewModel ViewModel { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        ViewModel.Dispose();
        Directory.Delete(directory, true);
    }
}
