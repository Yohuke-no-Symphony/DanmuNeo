using Yohuke.DanmuNeo.I18n;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 将可复用的业务视图显式连接到工作台上下文。
/// </summary>
public class WorkbenchRoomPanel : UserControl, IDisposable
{
    /// <summary>
    /// 显式上下文的绑定属性。
    /// </summary>
    public static readonly DirectProperty<WorkbenchRoomPanel, WorkbenchContext> ContextProperty =
        AvaloniaProperty.RegisterDirect<WorkbenchRoomPanel, WorkbenchContext>(nameof(Context), panel => panel.Context);

    private WorkbenchContext context = null!;
    private LiveRoomTabViewModel? observedRoom;
    private bool isDisposed;

    /// <summary>
    /// 未注入上下文的设计时实例不接收业务交互。
    /// </summary>
    public WorkbenchRoomPanel()
    {
        IsEnabled = false;
    }

    /// <summary>
    /// 由工作台显式注入的上下文。
    /// </summary>
    public WorkbenchContext Context => context;

    /// <summary>
    /// 初始化面板绑定；一个面板实例只能连接一次。
    /// </summary>
    protected void InitializeContext(WorkbenchContext context)
    {
        if (Context is not null)
        {
            throw new InvalidOperationException(Localization.Get("UI.WorkbenchRoomPanel.ContextAlreadyInitialized"));
        }

        SetAndRaise(ContextProperty, ref this.context, context);
        context.PropertyChanged += Context_OnPropertyChanged;
        SetRoom(context.CurrentRoom);
    }

    /// <summary>
    /// 切换当前房间的 UI 订阅，不改变业务运行状态。
    /// </summary>
    protected virtual void OnCurrentRoomChanged(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
    }

    /// <summary>
    /// 释放业务视图的事件和平台控件。
    /// </summary>
    protected virtual void DisposePanel()
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        if (context is not null)
        {
            context.PropertyChanged -= Context_OnPropertyChanged;
        }
        SetRoom(null);
        DisposePanel();
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
        observedRoom = room;
        DataContext = room;
        IsEnabled = room is not null;
        OnCurrentRoomChanged(oldRoom, room);
    }
}
