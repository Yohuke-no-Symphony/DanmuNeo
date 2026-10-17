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
    /// 内置、用户预设和操作分组使用真实分隔线，增删最后一个用户布局后不会留下空分组。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LayoutMenuSeparatesBuiltInUserPresetsAndActions(int userCount)
    {
        await using var context = new WorkbenchLayoutTestContext();
        using var catalog = new WorkbenchMenuCatalog(context.Controller, () => { }, () => { }, () => { },
            () => { }, () => { }, () => false);
        using var icons = new WorkbenchMenuIcons();
        var menu = new Menu();
        using var projection = new WorkbenchMenuProjection(catalog, icons, menu);
        var root = projection.NativeMenu;
        var userIds = new List<string>();
        var expected = new List<string> { "默认", "同传", "歌词", "转发", "|" };

        for (var index = 0; index < userCount; index++)
        {
            var name = $"我的布局{index + 1}";
            userIds.Add(context.Controller.SaveAs(name));
            expected.Add(name);
        }

        if (userCount > 0)
        {
            expected.Add("|");
        }

        expected.AddRange(["保存当前布局", "另存为布局…", "管理布局…", "恢复默认布局"]);
        AssertLayoutMenu(menu, root, expected);
        Assert.Same(root, projection.NativeMenu);

        if (userCount > 0)
        {
            Assert.True(context.Controller.Rename(userIds[0], "已重命名"));
            expected[5] = "已重命名";
            AssertLayoutMenu(menu, root, expected);
        }

        foreach (var id in userIds)
        {
            Assert.True(context.Controller.Delete(id));
        }

        AssertLayoutMenu(menu, root,
            ["默认", "同传", "歌词", "转发", "|", "保存当前布局", "另存为布局…", "管理布局…", "恢复默认布局"]);
        Assert.Same(root, projection.NativeMenu);
    }

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
            var nativeItems = nativeGroups[groupIndex].Menu!.Items.OfType<NativeMenuItem>()
                .Where(item => item is not NativeMenuItemSeparator).ToArray();
            var commands = group.Children!.Where(item => !item.IsSeparator).ToArray();

            for (var itemIndex = 0; itemIndex < commands.Length; itemIndex++)
            {
                var definition = commands[itemIndex];
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

        var entries = definitions.SelectMany(group => group.Children!).Where(item => !item.IsSeparator).ToArray();
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

    private static void AssertLayoutMenu(Menu menu, NativeMenu nativeMenu, IReadOnlyList<string> expected)
    {
        var windowLayouts = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "布局"));
        var nativeLayouts = nativeMenu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "布局");
        Assert.Equal(expected, windowLayouts.Items.Select(item => item switch
        {
            Separator => "|",
            MenuItem command => command.Header?.ToString(),
            _ => throw new InvalidOperationException("窗口菜单包含未知项目。")
        }));
        Assert.Equal(expected, nativeLayouts.Menu!.Items.Select(item => item switch
        {
            NativeMenuItemSeparator => "|",
            NativeMenuItem command => command.Header,
            _ => throw new InvalidOperationException("原生菜单包含未知项目。")
        }));
    }
}
