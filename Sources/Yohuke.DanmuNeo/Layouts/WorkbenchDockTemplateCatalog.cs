using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Recycling;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Settings;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Layouts;

internal static class WorkbenchDockTemplateCatalog
{
    internal static void Install(DockControl host, WorkbenchControlRecycling recycling)
    {
        host.PropertyChanged += OnHostPropertyChanged;
        host.AutoCreateDataTemplates = false;
        ControlRecyclingDataTemplate.SetControlRecycling(host, recycling);
        host.DataTemplates.Add(new WorkbenchDockPanelTemplate());
        host.DataTemplates.Add(new FuncDataTemplate<IRootDock>(value => value is not null, (_, _) => new RootDockControl()));
        host.DataTemplates.Add(new FuncDataTemplate<IProportionalDock>(value => value is not null, (_, _) => new WorkbenchProportionalDockControl()));
        host.DataTemplates.Add(new FuncDataTemplate<IToolDock>(value => value is not null, (_, _) => CreateToolDockControl()));
        host.DataTemplates.Add(new FuncDataTemplate<IProportionalDockSplitter>(value => value is not null, (_, _) => new WorkbenchProportionalStackPanelSplitter
        {
            [!ProportionalStackPanelSplitter.IsResizingEnabledProperty] = new Binding(nameof(IProportionalDockSplitter.CanResize)),
            [!ProportionalStackPanelSplitter.PreviewResizeProperty] = new Binding(nameof(IProportionalDockSplitter.ResizePreview))
        }));
    }

    private static void OnHostPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is DockControl host && e.Property == DockControl.IsDraggingDockProperty &&
            e.GetOldValue<bool>() && !e.GetNewValue<bool>())
        {
            WorkbenchTextInput.RequestNativeContextRecovery(host);
        }
    }

    private static ToolDockControl CreateToolDockControl()
    {
        return new()
        {
            Template = new FuncControlTemplate<ToolDockControl>((_, scope) =>
            {
                var tracking = new DockableControl
                {
                    Name = "PART_DockableControl",
                    TrackingMode = TrackingMode.Visible,
                    [~Panel.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty)
                }.RegisterInNameScope(scope);
                DockProperties.SetIsDropArea(tracking, true);
                DockProperties.SetIsDockTarget(tracking, true);
                var frame = new Border
                {
                    Name = "PART_WorkspaceFrame",
                    [!Avalonia.Visual.IsVisibleProperty] = new Binding("VisibleDockables.Count")
                    {
                        Converter = new FuncValueConverter<int, bool>(count => count > 0)
                    },
                    Child = new WorkbenchToolChromeControl
                    {
                        MinHeight = 0,
                        Content = new WorkbenchToolControl { MinHeight = 0 },
                        [!ToolChromeControl.IsActiveProperty] = new Binding(nameof(IToolDock.IsActive))
                    }
                }.RegisterInNameScope(scope);
                frame.Classes.Add("workbenchFrame");
                tracking.Children.Add(frame);
                return tracking;
            })
        };
    }
}
