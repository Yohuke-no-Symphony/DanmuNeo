using Avalonia.Controls;
using Avalonia.Input;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 独立同传输入面板，草稿与发送队列由房间视图模型持有。
/// </summary>
public partial class DraftComposerPanelView : WorkbenchRoomPanel
{
    /// <summary>
    /// 创建并连接输入面板。
    /// </summary>
    public DraftComposerPanelView(WorkbenchContext context) : this()
    {
        InitializeContext(context);
    }

    /// <summary>
    /// 创建供 XAML 加载器使用的面板实例，运行时通过带上下文的构造入口连接。
    /// </summary>
    public DraftComposerPanelView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 提供给工作台焦点和光标协调器的输入控件。
    /// </summary>
    public TextBox Input => InputDraftTextBox;

    private void InputDraft_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Enter || WorkbenchTextInput.IsComposing(sender) || Context.CurrentRoom is not { } room ||
            !room.SendDraftCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        room.SendDraftCommand.Execute(null);
    }

    private async void DanmuConfigSelector_OnDropDownOpened(object? sender, EventArgs e)
    {
        if (Context.CurrentRoom is { } room)
        {
            await room.RetryDanmuConfigurationAsync();
        }
    }
}
