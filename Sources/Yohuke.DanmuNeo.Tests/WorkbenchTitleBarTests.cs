using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证窗口菜单与标题共用一行，并保留独立的菜单点击区域。
/// </summary>
public class WorkbenchTitleBarTests
{
    /// <summary>
    /// 最小和常规尺寸下，长工作区标题、菜单与窗口按钮互不遮挡，切换菜单模式不增加标题行。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(980)]
    [InlineData(1280)]
    public async Task MenuSharesTitleRowAndStillOpensFromPointer(double width)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        context.ViewModel.Settings.WindowWidth = width;
        context.ViewModel.Settings.UseWindowMenuOnMac = true;
        var name = string.Concat(Enumerable.Repeat("很长的工作区名称", 8));
        context.ViewModel.SelectedWorkspace!.Name = name;
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
            var title = main.GetVisualDescendants().OfType<TitleBarView>().Single();
            var menu = main.FindControl<Menu>("WindowMenu")!;
            var buttons = title.FindControl<StackPanel>("WindowButtons")!;
            var titleBounds = new Rect(title.TranslatePoint(default, main)!.Value, title.Bounds.Size);
            var menuBounds = new Rect(menu.TranslatePoint(default, main)!.Value, menu.Bounds.Size);
            var buttonsBounds = new Rect(buttons.TranslatePoint(default, main)!.Value, buttons.Bounds.Size);
            var workspaceName = title.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == name);
            var nameBounds = new Rect(workspaceName.TranslatePoint(default, main)!.Value, workspaceName.Bounds.Size);
            Assert.Equal(42, titleBounds.Height);
            Assert.InRange(menuBounds.Top, titleBounds.Top, titleBounds.Bottom);
            Assert.InRange(menuBounds.Bottom, titleBounds.Top, titleBounds.Bottom);
            Assert.True(nameBounds.Right < menuBounds.Left);
            Assert.True(menuBounds.Right < buttonsBounds.Left);
            Assert.True(buttonsBounds.Right <= main.Bounds.Width);
            Assert.InRange(nameBounds.Width, 1, 180);
            var listen = main.FindControl<Button>("ListenToggleButton")!;
            var toolbarTop = listen.TranslatePoint(default, main)!.Value.Y;
            Assert.InRange(toolbarTop, 42, 54);
            var views = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "视图"));
            var point = views.TranslatePoint(new Point(views.Bounds.Width / 2, views.Bounds.Height / 2), main)!.Value;
            var position = main.Position;
            main.MouseMove(point);
            main.MouseDown(point, MouseButton.Left);
            main.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(views.IsSubMenuOpen);
            Assert.Equal(position, main.Position);
            views.IsSubMenuOpen = false;

            foreach (var windowMode in new[] { false, true })
            {
                context.ViewModel.UseWindowMenuOnMac = windowMode;
                Dispatcher.UIThread.RunJobs();
                main.UpdateLayout();
                Assert.Equal(42, title.Bounds.Height);
                Assert.Equal(toolbarTop, listen.TranslatePoint(default, main)!.Value.Y);
                Assert.Equal(windowMode || !OperatingSystem.IsMacOS(), menu.IsVisible);
            }
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }
}
