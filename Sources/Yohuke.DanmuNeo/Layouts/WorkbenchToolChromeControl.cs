using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchToolChromeControl : ToolChromeControl
{
    private Button? closeButton;

    protected override Type StyleKeyOverride => typeof(ToolChromeControl);

    /// <summary>
    /// 在宿主接线前接管面板关闭，并为标题按钮应用紧凑样式。
    /// </summary>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (closeButton is not null)
        {
            closeButton.Click -= OnCloseButtonClick;
        }

        closeButton = e.NameScope.Find<Button>("PART_CloseButton");

        if (closeButton is not null)
        {
            closeButton.Click += OnCloseButtonClick;
        }

        base.OnApplyTemplate(e);

        foreach (var name in new[]
                     { "PART_MenuButton", "PART_PinButton", "PART_MaximizeRestoreButton", "PART_CloseButton" })
        {
            if (e.NameScope.Find<Button>(name) is { } button)
            {
                button.Classes.Add("dockChromeButton");
            }
        }
    }

    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (DataContext is IDock { Factory: { } factory, ActiveDockable: { } dockable })
        {
            factory.CloseDockable(dockable);
        }
    }
}
