using Avalonia.Controls;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Mvvm.Controls;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchDockPanel : Tool, IDeferredContentPresentation
{
    internal WorkbenchDockPanel(string panelId, Control view)
    {
        Id = panelId;
        View = view;
        Title = GetLocalizedTitle(panelId);
        CanPin = false;
        CanClose = true;
        CanFloat = true;
        CanDockAsDocument = false;
    }

    public Control View { get; }
    public bool DeferContentPresentation => false;

    internal static string GetLocalizedTitle(string panelId)
    {
        return Localization.Get(panelId switch
        {
            WorkbenchPanelIds.WORKSPACE => "Workbench.Panel.Workspace",
            WorkbenchPanelIds.LIVE_PLAYER => "Workbench.Panel.LivePlayer",
            WorkbenchPanelIds.COMPOSER => "Workbench.Panel.Composer",
            WorkbenchPanelIds.SUPER_CHAT => "Workbench.Panel.SuperChat",
            WorkbenchPanelIds.DANMU => "Workbench.Panel.Danmu",
            WorkbenchPanelIds.HISTORY => "Workbench.Panel.History",
            WorkbenchPanelIds.LYRICS => "Workbench.Panel.Lyrics",
            WorkbenchPanelIds.FORWARDING => "Workbench.Panel.Forwarding",
            WorkbenchPanelIds.LOG => "Workbench.Panel.Log",
            WorkbenchPanelIds.MODERATION => "Workbench.Panel.Moderation",
            WorkbenchPanelIds.ACCOUNT_MEDALS => "Workbench.Panel.AccountMedals",
            WorkbenchPanelIds.LIVE_CAPTURES => "Workbench.Panel.LiveCaptures",
            _ => throw new ArgumentOutOfRangeException(nameof(panelId))
        });
    }
}
