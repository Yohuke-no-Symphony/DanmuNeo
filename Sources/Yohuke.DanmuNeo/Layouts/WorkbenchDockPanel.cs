using Avalonia.Controls;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Mvvm.Controls;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchDockPanel : Tool, IDeferredContentPresentation
{
    internal WorkbenchDockPanel(string panelId, Control view)
    {
        Id = panelId;
        View = view;
        Title = panelId switch
        {
            WorkbenchPanelIds.WORKSPACE => "工作区",
            WorkbenchPanelIds.LIVE_PLAYER => "追帧",
            WorkbenchPanelIds.COMPOSER => "输入框",
            WorkbenchPanelIds.SUPER_CHAT => "SC",
            WorkbenchPanelIds.DANMU => "弹幕",
            WorkbenchPanelIds.HISTORY => "历史",
            WorkbenchPanelIds.LYRICS => "歌词",
            WorkbenchPanelIds.FORWARDING => "转发",
            WorkbenchPanelIds.LOG => "Log",
            _ => throw new ArgumentException("未知的工作台面板。", nameof(panelId))
        };
        CanPin = false;
        CanClose = true;
        CanFloat = true;
        CanDockAsDocument = false;
    }

    public Control View { get; }
    public bool DeferContentPresentation => false;
}
