using Avalonia.Controls;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Windowing;

/// <summary>
/// 在独立面板和浮窗之间恢复输入焦点，并丢弃属于旧房间的延迟操作。
/// </summary>
public sealed class WorkbenchInteractionCoordinator
{
    private readonly WorkbenchContext context;
    private readonly DraftComposerPanelView composer;
    private Action<string>? activatePanel;

    /// <summary>
    /// 使用复用的输入面板连接工作台交互。
    /// </summary>
    public WorkbenchInteractionCoordinator(WorkbenchContext context, DraftComposerPanelView composer)
    {
        this.context = context;
        this.composer = composer;
    }

    /// <summary>
    /// 配置显示面板并激活所属窗口的工作台入口。
    /// </summary>
    public void SetPanelActivator(Action<string> activate)
    {
        activatePanel = activate;
    }

    /// <summary>
    /// 将指定房间的弹幕或 SC 内容插入当前草稿。
    /// </summary>
    public void InsertFeedContent(LiveRoomTabViewModel room, WorkbenchSelection selection, string content)
    {
        if (!context.IsCurrent(selection) || !ReferenceEquals(selection.Room, room))
        {
            return;
        }

        room.InputDraft = composer.Input.Text ?? "";
        var caret = room.InsertDanmuContent(content, composer.Input.CaretIndex);
        FocusInput(selection, caret);
    }

    /// <summary>
    /// 显示输入面板，并在相同房间中将光标移到草稿末尾。
    /// </summary>
    public void FocusInputDraft()
    {
        if (context.CaptureSelection() is { } selection)
        {
            FocusInput(selection, selection.Room.InputDraft.Length);
        }
    }

    /// <summary>
    /// 清空当前草稿而不改变已经提交的发送快照。
    /// </summary>
    public void ClearInputDraft()
    {
        context.CurrentRoom?.ClearInputDraft();
        composer.Input.Text = "";
        composer.Input.CaretIndex = 0;
    }

    private void FocusInput(WorkbenchSelection selection, int caret)
    {
        activatePanel?.Invoke(WorkbenchPanelIds.COMPOSER);
        Dispatcher.UIThread.Post(() =>
        {
            if (!context.IsCurrent(selection))
            {
                return;
            }

            if (TopLevel.GetTopLevel(composer) is Window owner)
            {
                owner.Activate();
            }

            composer.Input.Text = selection.Room.InputDraft;
            composer.Input.Focus();
            composer.Input.CaretIndex = Math.Clamp(caret, 0, composer.Input.Text?.Length ?? 0);
        }, DispatcherPriority.Background);
    }
}