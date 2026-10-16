using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 独立转发规则面板，隐藏和迁移不改变转发会话。
/// </summary>
public partial class ForwardingPanelView : WorkbenchRoomPanel
{
    /// <summary>
    /// 创建并连接转发面板。
    /// </summary>
    public ForwardingPanelView(WorkbenchContext context) : this()
    {
        InitializeContext(context);
    }

    /// <summary>
    /// 创建供 XAML 加载器使用的面板实例，运行时通过带上下文的构造入口连接。
    /// </summary>
    public ForwardingPanelView()
    {
        InitializeComponent();
    }
}
