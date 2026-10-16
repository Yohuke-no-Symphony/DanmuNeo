using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Windowing;

/// <summary>
/// 由工作台持有的强类型应用、工作区和当前直播间上下文。
/// </summary>
public sealed class WorkbenchContext : ObservableObject, IDisposable
{
    private readonly bool tracksSelection;
    private WorkspaceViewModel? currentWorkspace;
    private LiveRoomTabViewModel? currentRoom;
    private long selectionGeneration;
    private bool isDisposed;

    /// <summary>
    /// 观察应用选择，并统一激活当前直播间的发送配置。
    /// </summary>
    public WorkbenchContext(MainWindowViewModel application)
    {
        Application = application;
        tracksSelection = true;
        Application.PropertyChanged += Application_OnPropertyChanged;
        UpdateWorkspace();
    }

    internal WorkbenchContext(MainWindowViewModel application, LiveRoomTabViewModel? room)
    {
        Application = application;
        SetCurrentRoom(room);
    }

    /// <summary>
    /// 应用组合根。
    /// </summary>
    public MainWindowViewModel Application { get; }

    /// <summary>
    /// 所有业务面板共同展示的直播间。
    /// </summary>
    public LiveRoomTabViewModel? CurrentRoom => currentRoom;

    /// <summary>
    /// 直播间选择或身份每次变化时递增。
    /// </summary>
    public long SelectionGeneration => selectionGeneration;

    /// <summary>
    /// 当前是否选择了可供业务面板展示的直播间。
    /// </summary>
    public bool HasCurrentRoom => currentRoom is not null;

    /// <summary>
    /// 当前房间或其身份变化，取消正在按住的界面操作。
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// 捕获跨面板操作的来源。
    /// </summary>
    public WorkbenchSelection? CaptureSelection()
    {
        return currentRoom is null ? null : new(currentRoom, currentRoom.RoomId, selectionGeneration);
    }

    /// <summary>
    /// 检查操作仍属于相同直播间及选择代次。
    /// </summary>
    public bool IsCurrent(WorkbenchSelection? selection)
    {
        return !isDisposed && selection is not null &&
               ReferenceEquals(currentRoom, selection.Room) &&
               selection.RoomId == currentRoom.RoomId && selection.Generation == selectionGeneration;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        Application.PropertyChanged -= Application_OnPropertyChanged;

        if (currentWorkspace is not null)
        {
            currentWorkspace.PropertyChanged -= Workspace_OnPropertyChanged;
        }

        SetCurrentRoom(null);
    }

    internal void SetPreviewRoom(LiveRoomTabViewModel? room)
    {
        if (!tracksSelection && !isDisposed)
        {
            SetCurrentRoom(room);
        }
    }

    private void Application_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedWorkspace))
        {
            UpdateWorkspace();
        }
    }

    private void UpdateWorkspace()
    {
        if (currentWorkspace is not null)
        {
            currentWorkspace.PropertyChanged -= Workspace_OnPropertyChanged;
        }

        currentWorkspace = Application.SelectedWorkspace;

        if (currentWorkspace is not null)
        {
            currentWorkspace.PropertyChanged += Workspace_OnPropertyChanged;
        }

        SetCurrentRoom(currentWorkspace?.SelectedRoom);
    }

    private void Workspace_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.SelectedRoom))
        {
            SetCurrentRoom(currentWorkspace?.SelectedRoom);
        }
    }

    private void SetCurrentRoom(LiveRoomTabViewModel? room)
    {
        if (ReferenceEquals(currentRoom, room))
        {
            return;
        }

        if (currentRoom is not null)
        {
            currentRoom.PropertyChanged -= Room_OnPropertyChanged;
            currentRoom.Deactivate();
        }

        currentRoom = room;
        selectionGeneration++;

        if (currentRoom is not null)
        {
            currentRoom.PropertyChanged += Room_OnPropertyChanged;
            _ = currentRoom.ActivateDanmuConfigurationAsync();
        }

        OnPropertyChanged(nameof(SelectionGeneration));
        OnPropertyChanged(nameof(CurrentRoom));
        OnPropertyChanged(nameof(HasCurrentRoom));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Room_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveRoomTabViewModel.RoomId))
        {
            selectionGeneration++;
            OnPropertyChanged(nameof(SelectionGeneration));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
