using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证应用级工具与房间级面板在同一工作台中的绑定边界。</summary>
public class WorkbenchApplicationPanelTests
{
    /// <summary>未选房间及切换房间时，应用面板保持可用并保留自己的业务上下文。</summary>
    [AvaloniaFact]
    public void ApplicationPanelKeepsOwnContextWithoutCurrentRoom()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        var workspace = fixture.ViewModel.SelectedWorkspace!;
        workspace.SelectedRoom = null;
        var viewModel = new object();
        using var panel = new ApplicationPanelTestView(context, viewModel);

        Assert.True(panel.IsEnabled);
        Assert.Same(viewModel, panel.DataContext);
        workspace.SelectedRoom = fixture.Room;
        Assert.True(panel.IsEnabled);
        Assert.Same(viewModel, panel.DataContext);
        workspace.SelectedRoom = null;
        Assert.True(panel.IsEnabled);
        Assert.Same(viewModel, panel.DataContext);
    }

    /// <summary>房间面板继续跟随当前选择，无房间时禁用，恢复选择后重新启用。</summary>
    [AvaloniaFact]
    public void RoomPanelKeepsItsSelectionBindingContract()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new DraftComposerPanelView(context);
        var workspace = fixture.ViewModel.SelectedWorkspace!;

        Assert.Same(fixture.Room, panel.DataContext);
        workspace.SelectedRoom = null;
        Assert.False(panel.IsEnabled);
        Assert.Null(panel.DataContext);
        workspace.SelectedRoom = fixture.Room;
        Assert.True(panel.IsEnabled);
        Assert.Same(fixture.Room, panel.DataContext);
    }
}
