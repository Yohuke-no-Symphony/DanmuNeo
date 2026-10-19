using System.ComponentModel;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.ViewModels;

public partial class MainWindowViewModel
{
    private readonly MedalSwitchCoordinator medalSwitchCoordinator;
    private readonly CancellationTokenSource toolsLifetime = new();
    private WorkspaceViewModel? toolsWorkspace;
    private LiveRoomTabViewModel? toolsRoom;
    private bool toolsInitialized;
    private bool toolsStarted;
    private bool toolsDisposed;

    /// <summary>跟随当前账号或固定账号，管理全部已添加房间的禁言名单。</summary>
    public ModerationManagerViewModel ModerationManager { get; }

    /// <summary>默认跟随当前房间实际账号，亦可固定账号管理粉丝牌。</summary>
    public AccountMedalsViewModel AccountMedals { get; }

    /// <summary>独立于房间选择的永久直播截图图库。</summary>
    public LiveCapturesViewModel LiveCaptures { get; }

    /// <summary>当前播放器原帧抓取及原生焦点快捷键桥接。</summary>
    public ILiveFrameCaptureService FrameCaptureService { get; }

    internal Task PendingMedalSwitch { get; private set; } = Task.CompletedTask;
    internal Task GalleryInitialization { get; private set; } = Task.CompletedTask;

    /// <summary>工作台显示后启用应用工具；构造阶段不启动自动换牌请求。</summary>
    public Task StartToolsAsync()
    {
        if (toolsDisposed || toolsStarted)
        {
            return Task.CompletedTask;
        }

        toolsStarted = true;
        RefreshToolSelection();
        GalleryInitialization = InitializeCaptureGalleryAsync(toolsLifetime.Token);
        return Task.CompletedTask;
    }

    internal async Task FlushCapturesAsync()
    {
        LiveCaptures.CancelExport();
        await GalleryInitialization;
        await LiveCaptures.FlushAsync();
    }

    private async Task InitializeCaptureGalleryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await LiveCaptures.InitializeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!toolsDisposed)
            {
                LiveCaptures.ReportCaptureFailure(exception.Message);
                Log.Error(Localization.Get("Captures.Source"), Localization.Get("Captures.LoadFailedLog"), exception);
            }
        }
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
        ModerationManager.UpdateCurrentAccount(account);
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
        toolsLifetime.Cancel();
        toolsLifetime.Dispose();
        LiveCaptures.Dispose();
    }
}
