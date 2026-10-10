using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用离线依赖和临时配置验证工作区预设轮切。
/// </summary>
public sealed class MarkGroupCycleTestContext : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 创建两个工作区和三个按顺序显示的同传预设。
    /// </summary>
    public MarkGroupCycleTestContext(bool hasRoom = true)
    {
        StateService = new(directory);
        StateService.Save(new()
        {
            Settings = new()
            {
                MarkGroups =
                [
                    new() { Id = "a", Name = "默认样式", SortOrder = 0 },
                    new() { Id = "b", Name = "方括号", TranslateOpenMark = "[", TranslateCloseMark = "]", SortOrder = 1 },
                    new() { Id = "c", Name = "引号", TranslateOpenMark = "「", TranslateCloseMark = "」", SortOrder = 2 }
                ]
            },
            Workspaces =
            [
                new()
                {
                    Name = "当前工作区", SelectedMarkGroupId = "a", SortOrder = 0,
                    LiveRooms = hasRoom ? [new() { RoomId = "100", RoomName = "离线房间" }] : []
                },
                new() { Name = "其他工作区", SelectedMarkGroupId = "c", SortOrder = 1 }
            ]
        });
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
