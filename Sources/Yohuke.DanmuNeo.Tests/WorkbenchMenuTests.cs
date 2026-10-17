using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Core;
using Lucide.Avalonia;
using SkiaSharp;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Menus;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证视图菜单始终打开并激活面板，布局选中状态独立保留。
/// </summary>
public class WorkbenchMenuTests
{
    /// <summary>
    /// 顶层始终显示四个文字标题，展开后的面板菜单同时显示图标和文字。
    /// </summary>
    [AvaloniaFact]
    public async Task MenuBarKeepsTextHeadingsAndSubmenuShowsIconsWithText()
    {
        await using var context = new WorkbenchLayoutTestContext();
        using var catalog = new WorkbenchMenuCatalog(context.Controller, () => { }, () => { }, () => { },
            () => { }, () => { }, () => false);
        using var icons = new WorkbenchMenuIcons();
        var menu = new Menu();
        using var projection = new WorkbenchMenuProjection(catalog, icons, menu);
        DockPanel.SetDock(menu, Avalonia.Controls.Dock.Top);
        context.Owner.Content = null;
        context.Owner.Content = new DockPanel { Children = { menu, context.Controller.Host } };
        context.Show();
        var groups = menu.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(new[] { "应用", "视图", "布局", "帮助" }, groups.Select(item => item.Header));

        foreach (var group in groups)
        {
            Assert.Null(group.Icon);
            Assert.Contains(group.GetVisualDescendants().OfType<TextBlock>(),
                text => Equals(text.Text, group.Header) && text.IsEffectivelyVisible && text.Bounds.Width > 0);
        }

        var views = groups.Single(item => Equals(item.Header, "视图"));
        views.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();

        try
        {
            foreach (var item in views.Items.OfType<MenuItem>())
            {
                var icon = Assert.IsType<LucideIcon>(item.Icon);
                Assert.True(icon.IsEffectivelyVisible && icon.Bounds.Width > 0);
                Assert.Contains(item.GetVisualDescendants().OfType<TextBlock>(),
                    text => Equals(text.Text, item.Header) && text.IsEffectivelyVisible && text.Bounds.Width > 0);
            }
        }
        finally
        {
            views.IsSubMenuOpen = false;
        }
    }

    /// <summary>
    /// 两种菜单共享图标语义和命令，每个窗口独占控件；原生图标按描边生成可见的透明位图。
    /// </summary>
    [AvaloniaFact]
    public async Task WindowAndNativeMenusUseTheSameDistinctIconDefinitions()
    {
        await using var context = new WorkbenchLayoutTestContext();
        using var catalog = new WorkbenchMenuCatalog(context.Controller, () => { }, () => { }, () => { },
            () => { }, () => { }, () => false);
        using var icons = new WorkbenchMenuIcons();
        var firstMenu = new Menu();
        var secondMenu = new Menu();
        using var first = new WorkbenchMenuProjection(catalog, icons, firstMenu);
        using var second = new WorkbenchMenuProjection(catalog, icons, secondMenu);
        var customId = context.Controller.SaveAs("带图标的布局");
        var definitions = catalog.Entries;
        var firstGroups = firstMenu.Items.OfType<MenuItem>().ToArray();
        var secondGroups = secondMenu.Items.OfType<MenuItem>().ToArray();
        var nativeGroups = first.NativeMenu.Items.OfType<NativeMenuItem>().ToArray();

        for (var groupIndex = 0; groupIndex < definitions.Count; groupIndex++)
        {
            var group = definitions[groupIndex];
            Assert.Null(group.Icon);
            Assert.Null(firstGroups[groupIndex].Icon);
            Assert.Null(secondGroups[groupIndex].Icon);
            Assert.Null(nativeGroups[groupIndex].Icon);
            Assert.Equal(group.Header, firstGroups[groupIndex].Header);
            Assert.Equal(group.Header, nativeGroups[groupIndex].Header);
            var firstItems = firstGroups[groupIndex].Items.OfType<MenuItem>().ToArray();
            var secondItems = secondGroups[groupIndex].Items.OfType<MenuItem>().ToArray();
            var nativeItems = nativeGroups[groupIndex].Menu!.Items.OfType<NativeMenuItem>().ToArray();

            for (var itemIndex = 0; itemIndex < group.Children!.Count; itemIndex++)
            {
                var definition = group.Children[itemIndex];
                var windowItem = firstItems[itemIndex];
                var kind = Assert.IsType<LucideIconKind>(definition.Icon);
                Assert.Equal(definition.Header, windowItem.Header);
                Assert.Equal(definition.Header, nativeItems[itemIndex].Header);
                Assert.Equal(definition.Icon, Assert.IsType<LucideIcon>(windowItem.Icon).Kind);
                Assert.NotSame(windowItem.Icon, secondItems[itemIndex].Icon);
                Assert.Same(definition.Command, windowItem.Command);
                Assert.Same(definition.Command, nativeItems[itemIndex].Command);
                Assert.Same(icons.GetNative(kind), nativeItems[itemIndex].Icon);
            }
        }

        var entries = definitions.SelectMany(group => group.Children!).ToArray();
        Assert.Equal(8, entries.Where(item => item.Id.StartsWith("panel:")).Select(item => item.Icon).Distinct().Count());
        Assert.Equal(4, entries.Where(item => item.Id.StartsWith("layout-")).Select(item => item.Icon).Distinct().Count());
        Assert.Equal(LucideIconKind.LayoutTemplate, entries.Single(item => item.Id == $"layout:{customId}").Icon);

        foreach (var kind in entries.Select(item => Assert.IsType<LucideIconKind>(item.Icon)).Distinct())
        {
            using var stream = new MemoryStream();
            icons.GetNative(kind).Save(stream);
            using var image = SKBitmap.Decode(stream.ToArray());
            Assert.Equal(24, image.Width);
            Assert.Equal(24, image.Height);
            Assert.Contains(image.Pixels, pixel => pixel.Alpha > 0);
            Assert.Contains(image.Pixels, pixel => pixel.Alpha == 0);
        }
    }

    /// <summary>
    /// 已关闭、标签内和浮动的面板共用普通菜单命令，重复执行不会隐藏或复制面板。
    /// </summary>
    [AvaloniaFact]
    public async Task ViewMenuOpensOrFocusesWithoutCheckmarks()
    {
        await using var context = new WorkbenchLayoutTestContext();
        using var catalog = new WorkbenchMenuCatalog(context.Controller, () => { }, () => { }, () => { },
            () => { }, () => { }, () => false);
        var menu = new Menu();
        using var icons = new WorkbenchMenuIcons();
        using var projection = new WorkbenchMenuProjection(catalog, icons, menu);
        context.Show();
        var views = catalog.Entries.Single(group => group.Id == "views").Children!;
        var windowViews = menu.Items.OfType<MenuItem>().Single(group => Equals(group.Header, "视图"));
        var nativeViews = projection.NativeMenu.Items.OfType<NativeMenuItem>().Single(group => group.Header == "视图");
        Assert.All(views, item =>
        {
            Assert.False(item.IsCheckable);
            Assert.False(item.IsChecked);
        });
        Assert.All(windowViews.Items.OfType<MenuItem>(), item => Assert.Equal(MenuItemToggleType.None, item.ToggleType));
        Assert.All(nativeViews.Menu!.Items.OfType<NativeMenuItem>(), item =>
            Assert.Equal(NativeMenuItemToggleType.None, item.ToggleType));
        var id = WorkbenchPanelIds.HISTORY;
        var panel = context.Controller.PanelAdapters[id];
        var command = views.Single(item => item.Id == $"panel:{id}").Command!;
        context.Controller.HidePanel(id);
        context.Pump();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            command.Execute(null);
            context.Pump();
            Assert.True(context.Controller.IsVisible(id));
            Assert.Same(panel, context.Controller.PanelAdapters[id]);
            Assert.Same(panel, Assert.IsAssignableFrom<IDock>(panel.Owner).ActiveDockable);
            Assert.Equal(id, context.Controller.Capture().FocusedPanelId);
        }

        context.Controller.FloatPanel(id);
        context.Pump();
        var floating = Assert.Single(context.Controller.FloatingWindows);
        context.Owner.Activate();
        command.Execute(null);
        context.Pump();
        Assert.True(floating.IsActive);
        Assert.True(context.Controller.IsVisible(id));
        Assert.Same(panel, Assert.IsAssignableFrom<IDock>(panel.Owner).ActiveDockable);
        Assert.Single(context.Controller.FloatingWindows);
        var layouts = catalog.Entries.Single(group => group.Id == "layouts").Children!
            .Where(item => item.Id.StartsWith("layout:")).ToArray();
        Assert.All(layouts, item => Assert.True(item.IsCheckable));
        Assert.Equal($"layout:{context.Controller.CurrentPresetId}", Assert.Single(layouts, item => item.IsChecked).Id);
    }
}
