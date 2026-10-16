using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
using Dock.Settings;
using DockOrientation = Dock.Model.Core.Orientation;
using LayoutOrientation = Avalonia.Layout.Orientation;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchProportionalDockControl : ProportionalDockControl
{
    protected override Type StyleKeyOverride => typeof(ProportionalDockControl);

    /// <summary>
    /// 保留 Dock 模板和拖动入口，仅替换不会回写临时尺寸约束的分配容器。
    /// </summary>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var items = e.NameScope.Find<ItemsControl>("PART_ItemsControl")!;
        items.ItemsPanel = new FuncTemplate<Panel?>(() =>
        {
            var panel = new WorkbenchProportionalStackPanel
            {
                Background = Brushes.Transparent,
                [!ProportionalStackPanel.OrientationProperty] = new Binding("Orientation")
                {
                    Converter = new FuncValueConverter<DockOrientation, LayoutOrientation>(orientation =>
                        orientation == DockOrientation.Vertical ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal)
                }
            };
            DockProperties.SetIsDropArea(panel, true);
            return panel;
        });
    }
}
