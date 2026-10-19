using System.ComponentModel;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.ViewModels;

public partial class MainWindowViewModel
{
    private readonly MedalSwitchCoordinator medalSwitchCoordinator;
    private WorkspaceViewModel? toolsWorkspace;
    private LiveRoomTabViewModel? toolsRoom;
    private bool toolsInitialized;
    private bool toolsStarted;
    private bool toolsDisposed;

    /// <summary>在明确选择的账号下管理已添加房间的禁言名单。</summary>
    public ModerationManagerViewModel ModerationManager { get; }

    /// <summary>默认跟随当前房间实际账号，亦可固定账号管理粉丝牌。</summary>
    public AccountMedalsViewModel AccountMedals { get; }

    internal Task PendingMedalSwitch { get; private set; } = Task.CompletedTask;

    /// <summary>工作台显示后启用应用工具；构造阶段不启动自动换牌请求。</summary>
    public Task StartToolsAsync()
    {
        if (toolsDisposed || toolsStarted)
        {
            return Task.CompletedTask;
        }

        toolsStarted = true;
        RefreshToolSelection();
        return Task.CompletedTask;
    }

    private TimeSpan GetToolsTimeout()
    {
        return TimeSpan.FromSeconds(double.IsFinite(Settings.TimeoutSeconds)
            ? Math.Clamp(Settings.TimeoutSeconds, 0.1, 120) : 3.05);
    }

    private void RefreshToolSelection()
    {
        if (!toolsInitialized || toolsDisposed)
        {
            return;
        }

        if (!ReferenceEquals(toolsWorkspace, SelectedWorkspace))
        {
            if (toolsWorkspace is not null)
            {
                toolsWorkspace.PropertyChanged -= OnToolsWorkspaceChanged;
            }

            toolsWorkspace = SelectedWorkspace;
            if (toolsWorkspace is not null)
            {
                toolsWorkspace.PropertyChanged += OnToolsWorkspaceChanged;
            }
        }

        var nextRoom = toolsWorkspace?.SelectedRoom;
        if (!ReferenceEquals(toolsRoom, nextRoom))
        {
            if (toolsRoom is not null)
            {
                toolsRoom.PropertyChanged -= OnToolsRoomChanged;
            }

            toolsRoom = nextRoom;
            if (toolsRoom is not null)
            {
                toolsRoom.PropertyChanged += OnToolsRoomChanged;
            }
        }

        var account = toolsRoom is null ? null : ResolveAccount(toolsRoom);
        AccountMedals.UpdateCurrentAccount(account);
        if (toolsStarted)
        {
            PendingMedalSwitch = medalSwitchCoordinator.SetActiveRoomAsync(account, toolsRoom?.RoomId);
        }
    }

    private void OnToolsWorkspaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.SelectedRoom) or nameof(WorkspaceViewModel.AccountOverrideId))
        {
            RefreshToolSelection();
        }
    }

    private void OnToolsRoomChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LiveRoomTabViewModel.RoomId) or nameof(LiveRoomTabViewModel.AccountOverrideId))
        {
            RefreshToolSelection();
        }
    }

    private void DisposeTools()
    {
        if (toolsDisposed)
        {
            return;
        }

        toolsDisposed = true;
        if (toolsWorkspace is not null)
        {
            toolsWorkspace.PropertyChanged -= OnToolsWorkspaceChanged;
        }

        if (toolsRoom is not null)
        {
            toolsRoom.PropertyChanged -= OnToolsRoomChanged;
        }

        ModerationManager.Dispose();
        AccountMedals.Dispose();
        medalSwitchCoordinator.Dispose();
    }
}
