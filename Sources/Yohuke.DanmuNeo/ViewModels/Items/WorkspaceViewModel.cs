using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>
/// 工作区视图模型。
/// </summary>
public partial class WorkspaceViewModel : ViewModelBase, IDisposable
{
    private readonly Func<string?, string> getAccountName;
    private bool isDisposed;

    /// <summary>
    /// 初始化工作区视图模型。
    /// </summary>
    public WorkspaceViewModel(WorkspaceState state, Func<string?, string> getAccountName)
    {
        State = state;
        this.getAccountName = getAccountName;
        Rooms = new(state.LiveRooms.OrderBy(room => room.SortOrder).Select(room => new LiveRoomTabViewModel(room)));
        selectedRoom = Rooms.FirstOrDefault(room => room.Id == state.SelectedLiveRoomId) ?? Rooms.FirstOrDefault();
        Localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// 工作区状态。
    /// </summary>
    public WorkspaceState State { get; }

    /// <summary>
    /// 直播间标签页。
    /// </summary>
    public ObservableCollection<LiveRoomTabViewModel> Rooms { get; }

    /// <summary>
    /// 工作区 ID。
    /// </summary>
    public string Id => State.Id;

    /// <summary>
    /// 工作区名称。
    /// </summary>
    public string Name
    {
        get => State.Name;
        set
        {
            if (State.Name == value)
            {
                return;
            }

            State.Name = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 账号覆盖 ID。
    /// </summary>
    public string? AccountOverrideId
    {
        get => State.AccountOverrideId;
        set
        {
            if (State.AccountOverrideId == value)
            {
                return;
            }

            State.AccountOverrideId = value;

            foreach (var room in Rooms)
            {
                room.NotifyDanmuConfigurationAccountChanged();
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(AccountLabel));
        }
    }

    /// <summary>
    /// 当前使用的符号组 ID。
    /// </summary>
    public string? SelectedMarkGroupId
    {
        get => State.SelectedMarkGroupId;
        set
        {
            if (State.SelectedMarkGroupId == value)
            {
                return;
            }

            State.SelectedMarkGroupId = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 工作区账号提示。
    /// </summary>
    public string AccountLabel => getAccountName(AccountOverrideId);

    /// <summary>
    /// 直播间数量文本。
    /// </summary>
    public string RoomCountText => Localization.Format("Workspace.RoomCount", Rooms.Count);

    [ObservableProperty] private bool isExpanded = true;

    [ObservableProperty] private bool isSelected;

    [ObservableProperty] private LiveRoomTabViewModel? selectedRoom;

    partial void OnSelectedRoomChanged(LiveRoomTabViewModel? value)
    {
        State.SelectedLiveRoomId = value?.Id;
        RefreshRoomSelection();
    }

    /// <summary>
    /// 添加直播间。
    /// </summary>
    public LiveRoomTabViewModel AddRoom(string roomId, string roomName, string ownerUid = "")
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var roomState = new LiveRoomTabState
        {
            RoomId = roomId,
            RoomName = string.IsNullOrWhiteSpace(roomName) ? roomId : roomName,
            OwnerUid = ownerUid,
            SortOrder = Rooms.Count
        };
        State.LiveRooms.Add(roomState);
        var room = new LiveRoomTabViewModel(roomState);
        Rooms.Add(room);
        SelectedRoom = room;
        OnPropertyChanged(nameof(RoomCountText));
        RefreshRoomSelection();
        return room;
    }

    /// <summary>
    /// 删除直播间。
    /// </summary>
    public void RemoveRoom(LiveRoomTabViewModel room)
    {
        if (!Rooms.Contains(room))
        {
            return;
        }

        room.Dispose();
        State.LiveRooms.Remove(room.State);
        Rooms.Remove(room);
        SelectedRoom = Rooms.FirstOrDefault();
        OnPropertyChanged(nameof(RoomCountText));
        RefreshRoomSortOrder();
        RefreshRoomSelection();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;

        foreach (var room in Rooms)
        {
            room.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 移动直播间排序。
    /// </summary>
    public bool MoveRoom(LiveRoomTabViewModel room, int offset)
    {
        var oldIndex = Rooms.IndexOf(room);
        var newIndex = oldIndex + offset;

        if (oldIndex < 0 || newIndex < 0 || newIndex >= Rooms.Count)
        {
            return false;
        }

        Rooms.Move(oldIndex, newIndex);
        RefreshRoomSortOrder();
        return true;
    }

    /// <summary>
    /// 刷新账号显示。
    /// </summary>
    public void RefreshAccountLabel()
    {
        OnPropertyChanged(nameof(AccountLabel));
    }

    /// <summary>
    /// 刷新直播间选中状态。
    /// </summary>
    public void RefreshRoomSelection()
    {
        foreach (var room in Rooms)
        {
            room.IsSelected = IsSelected && room == SelectedRoom;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(RoomCountText));
        OnPropertyChanged(nameof(AccountLabel));
    }

    private void RefreshRoomSortOrder()
    {
        State.LiveRooms.Clear();

        for (var index = 0; index < Rooms.Count; index++)
        {
            Rooms[index].State.SortOrder = index;
            State.LiveRooms.Add(Rooms[index].State);
        }
    }
}
