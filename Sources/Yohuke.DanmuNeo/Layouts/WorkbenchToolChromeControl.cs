using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Dock.Avalonia.Controls;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchToolChromeControl : ToolChromeControl
{
    protected override Type StyleKeyOverride => typeof(ToolChromeControl);

    /// <summary>
    /// 为 Dock 标题按钮应用紧凑样式，并在窗口操作组添加浮窗独立置顶入口。
    /// </summary>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        foreach (var name in new[]
                     { "PART_MenuButton", "PART_PinButton", "PART_MaximizeRestoreButton", "PART_CloseButton" })
        {
            if (e.NameScope.Find<Button>(name) is { } button)
            {
                button.Classes.Add("dockChromeButton");
            }
        }

        if (e.NameScope.Find<Button>("PART_MenuButton")?.Parent is StackPanel actions)
        {
            actions.Children.Insert(0, new FloatingWindowPinButton());
        }
    }
}
