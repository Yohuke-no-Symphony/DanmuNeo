using System.ComponentModel;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 将可复用的业务视图显式连接到工作台上下文。
/// </summary>
public class WorkbenchRoomPanel : WorkbenchPanelView
{
    private LiveRoomTabViewModel? observedRoom;

    /// <inheritdoc/>
    protected override void OnContextInitialized()
    {
        Context.PropertyChanged += Context_OnPropertyChanged;
        SetRoom(Context.CurrentRoom);
    }

    /// <summary>
    /// 在旧房间的数据上下文解除前结束由界面持有的编辑事务。
    /// </summary>
    protected virtual void OnCurrentRoomChanging(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
    }

    /// <summary>
    /// 切换当前房间的 UI 订阅，不改变业务运行状态。
    /// </summary>
    protected virtual void OnCurrentRoomChanged(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
    }

    /// <inheritdoc/>
    protected override void OnContextReleased()
    {
        if (Context is not null)
        {
            Context.PropertyChanged -= Context_OnPropertyChanged;
        }
        SetRoom(null);
    }

    private void Context_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkbenchContext.CurrentRoom))
        {
            SetRoom(Context.CurrentRoom);
        }
    }

    private void SetRoom(LiveRoomTabViewModel? room)
    {
        var oldRoom = observedRoom;
        OnCurrentRoomChanging(oldRoom, room);
        observedRoom = room;
        DataContext = room;
        IsEnabled = room is not null;
        OnCurrentRoomChanged(oldRoom, room);
    }
}
