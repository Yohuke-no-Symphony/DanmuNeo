using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Yohuke.DanmuNeo.Views.Components;

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

        if (closeButton?.Parent is StackPanel headerButtons)
        {
            headerButtons.Children.Insert(0, new ContentControl
            {
                Name = "PART_HeaderActions",
                VerticalContentAlignment = VerticalAlignment.Center,
                [!ContentProperty] = new Binding(nameof(IDock.ActiveDockable)),
                ContentTemplate = new FuncDataTemplate<WorkbenchDockPanel>((panel, _) => CreateHeaderActions(panel))
            });
        }

        foreach (var name in new[]
                     { "PART_MenuButton", "PART_PinButton", "PART_MaximizeRestoreButton", "PART_CloseButton" })
        {
            if (e.NameScope.Find<Button>(name) is { } button)
            {
                button.Classes.Add("dockChromeButton");
            }
        }
    }

    private static Control? CreateHeaderActions(WorkbenchDockPanel? panel)
    {
        if (panel?.View is not WorkbenchPanelView { HeaderActionsTemplate: { } template } view)
        {
            return null;
        }

        var actions = template.Build(view);

        if (actions is not null)
        {
            actions.DataContext = view;
        }

        return actions;
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
