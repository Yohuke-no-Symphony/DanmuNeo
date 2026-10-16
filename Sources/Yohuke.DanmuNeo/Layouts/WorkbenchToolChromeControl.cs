using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Dock.Avalonia.Controls;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchToolChromeControl : ToolChromeControl
{
    protected override Type StyleKeyOverride => typeof(ToolChromeControl);

    /// <summary>
    /// 为 Dock 标题按钮标记独立样式，避免业务按钮尺寸影响紧凑窗口操作。
    /// </summary>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        foreach (var name in new[] { "PART_MenuButton", "PART_PinButton", "PART_MaximizeRestoreButton", "PART_CloseButton" })
        {
            if (e.NameScope.Find<Button>(name) is { } button)
            {
                button.Classes.Add("dockChromeButton");
            }
        }
    }
}
