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
    /// 长标题、菜单及置顶操作避开原生按钮保留区，指针命中和标题高度变化保持一致。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(980, 92, 0)]
    [InlineData(980, 0, 146)]
    [InlineData(980, 92, 146)]
    [InlineData(1280, 92, 0)]
    [InlineData(1280, 0, 146)]
    [InlineData(1280, 92, 146)]
    public async Task MenuSharesTitleRowAndStillOpensFromPointer(double width, double leftInset, double rightInset)
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
            var title = main.GetVisualDescendants().OfType<TitleBarView>().Single();
            title.CaptionInsets = new(leftInset, 0, rightInset, 0);
            PumpLayout(main);
            var menu = main.FindControl<Menu>("WindowMenu")!;
            var buttons = title.FindControl<StackPanel>("WindowButtons")!;
            var titleBounds = GetBounds(title, main);
            var menuBounds = GetBounds(menu, main);
            var buttonsBounds = GetBounds(buttons, main);
            var workspaceName = title.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == name);
            var nameBounds = GetBounds(workspaceName, main);
            Assert.Equal(SystemDecorations.Full, main.SystemDecorations);
            Assert.Equal(42, titleBounds.Height);
            Assert.InRange(menuBounds.Top, titleBounds.Top, titleBounds.Bottom);
            Assert.InRange(menuBounds.Bottom, titleBounds.Top, titleBounds.Bottom);
            Assert.True(nameBounds.Left >= titleBounds.Left + leftInset);
            Assert.True(nameBounds.Right < menuBounds.Left);
            Assert.True(menuBounds.Right < buttonsBounds.Left);
            Assert.True(buttonsBounds.Right <= titleBounds.Right - rightInset);
            Assert.InRange(nameBounds.Width, 1, 180);
            Assert.All(buttons.Children.OfType<Button>(), button =>
            {
                Assert.Contains(button.Name, new[] { "DebugToolsButton", "PinButton" });

                if (button.IsEffectivelyVisible)
                {
                    Assert.False(title.IsDragRegion(GetBounds(button, main).Center));
                }
            });
            Assert.True(title.IsDragRegion(nameBounds.Center));
            Assert.False(title.IsDragRegion(menuBounds.Center));

            if (leftInset > 0)
            {
                Assert.False(title.IsDragRegion(new(titleBounds.Left + leftInset / 2, titleBounds.Center.Y)));
            }

            if (rightInset > 0)
            {
                Assert.False(title.IsDragRegion(new(titleBounds.Right - rightInset / 2, titleBounds.Center.Y)));
            }

            var listen = main.FindControl<Button>("ListenToggleButton")!;
            var toolbarTop = listen.TranslatePoint(default, main)!.Value.Y;
            Assert.InRange(toolbarTop, 42, 54);
            Assert.False(title.IsDragRegion(GetBounds(listen, main).Center));
            var views = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "视图"));
            var point = GetBounds(views, main).Center;
            Assert.False(title.IsDragRegion(point));
            var position = main.Position;
            Click(main, point);
            Assert.True(views.IsSubMenuOpen);
            Assert.Equal(position, main.Position);
            views.IsSubMenuOpen = false;
            PumpLayout(main);
            var pin = title.FindControl<Button>("PinButton")!;
            var pinPoint = GetBounds(pin, main).Center;
            Assert.False(title.IsDragRegion(pinPoint));
            Assert.False(main.Topmost);
            Click(main, pinPoint);
            Assert.True(main.Topmost);
            Assert.Contains("selected", pin.Classes);
            Assert.Equal(position, main.Position);
            Click(main, pinPoint);
            Assert.False(main.Topmost);
            Assert.DoesNotContain("selected", pin.Classes);

            title.Height = 56;
            PumpLayout(main);
            Assert.Equal(56, title.Bounds.Height);
            Assert.Equal(toolbarTop + 14, listen.TranslatePoint(default, main)!.Value.Y);
            Assert.InRange(GetBounds(menu, main).Bottom, 0, GetBounds(title, main).Bottom);

            foreach (var windowMode in new[] { false, true })
            {
                context.ViewModel.UseWindowMenuOnMac = windowMode;
                PumpLayout(main);
                Assert.Equal(56, title.Bounds.Height);
                Assert.Equal(toolbarTop + 14, listen.TranslatePoint(default, main)!.Value.Y);
                Assert.Equal(windowMode || !OperatingSystem.IsMacOS(), menu.IsVisible);
                Assert.True(title.IsDragRegion(GetBounds(workspaceName, main).Center));
                Assert.False(title.IsDragRegion(GetBounds(pin, main).Center));
            }
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    private static Rect GetBounds(Control control, Window window)
    {
        return new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
    }

    private static void Click(Window window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
