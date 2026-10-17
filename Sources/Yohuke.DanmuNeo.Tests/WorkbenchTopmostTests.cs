using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证主窗口置顶联动和浮动业务窗的独立置顶行为。
/// </summary>
public class WorkbenchTopmostTests
{
    /// <summary>
    /// 浮窗标题栏按钮只切换所在窗口，主窗口和其他浮窗维持原状态。
    /// </summary>
    [AvaloniaFact]
    public async Task FloatingPinButtonOnlyChangesItsOwnWindow()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
            PumpLayout(main);
            var first = Assert.Single(workbench.Layouts.FloatingWindows);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.LYRICS);
            PumpLayout(main);
            var second = workbench.Layouts.FloatingWindows.Single(window => !ReferenceEquals(window, first));
            var button = Assert.Single(first.GetVisualDescendants().OfType<Button>(),
                control => control.Name == "WindowTopmostButton" && control.IsEffectivelyVisible);
            Assert.DoesNotContain(main.GetVisualDescendants().OfType<Button>(),
                control => control.Name == "WindowTopmostButton" && control.IsEffectivelyVisible);
            Assert.Equal(button.Bounds.Width, button.Bounds.Height);
            AssertButtonFitsTitleBar(button);

            ClickButton(first, button);

            Assert.True(first.Topmost);
            Assert.Contains("selected", button.Classes);
            Assert.False(main.Topmost);
            Assert.False(second.Topmost);
            CaptureFloatingWindow(first, "independently-pinned");

            ClickButton(first, button);

            Assert.False(first.Topmost);
            Assert.DoesNotContain("selected", button.Classes);
            Assert.False(main.Topmost);
            Assert.False(second.Topmost);
            CaptureFloatingWindow(first, "unpinned");
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 全局置顶的往返切换保留独立选择，在全局开启时取消独立选择也不会立即解除全局置顶。
    /// </summary>
    [AvaloniaFact]
    public async Task GlobalPinKeepsFloatingIndependentChoice()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
            PumpLayout(main);
            var first = Assert.Single(workbench.Layouts.FloatingWindows);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.LYRICS);
            PumpLayout(main);
            var second = workbench.Layouts.FloatingWindows.Single(window => !ReferenceEquals(window, first));
            var button = Assert.Single(first.GetVisualDescendants().OfType<Button>(),
                control => control.Name == "WindowTopmostButton" && control.IsEffectivelyVisible);
            ClickButton(first, button);

            ClickPin(main);

            Assert.True(first.Topmost);
            Assert.True(second.Topmost);

            ClickPin(main);

            Assert.True(first.Topmost);
            Assert.False(second.Topmost);
            Assert.Contains("selected", button.Classes);

            ClickPin(main);
            ClickButton(first, button);

            Assert.True(first.Topmost);
            Assert.DoesNotContain("selected", button.Classes);
            Assert.Contains("跟随主窗口", Assert.IsType<string>(ToolTip.GetTip(button)));
            CaptureFloatingWindow(first, "following-main-window");

            ClickPin(main);

            Assert.False(first.Topmost);
            Assert.False(second.Topmost);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 顶部按钮同步已有和新建浮窗，取消置顶后关闭的浮窗不再参与同步。
    /// </summary>
    [AvaloniaFact]
    public async Task PinButtonUpdatesExistingAndNewFloatingWindows()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
            PumpLayout(main);
            var first = Assert.Single(workbench.Layouts.FloatingWindows);
            Assert.False(first.Topmost);

            ClickPin(main);

            Assert.True(main.Topmost);
            Assert.True(first.Topmost);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.LYRICS);
            PumpLayout(main);
            Assert.Equal(2, workbench.Layouts.FloatingWindows.Count);
            Assert.All(workbench.Layouts.FloatingWindows, window => Assert.True(window.Topmost));

            ClickPin(main);

            Assert.False(main.Topmost);
            Assert.All(workbench.Layouts.FloatingWindows, window => Assert.False(window.Topmost));
            first.Close();
            PumpLayout(main);
            Assert.DoesNotContain(first, workbench.Windows.BusinessWindows);

            ClickPin(main);

            Assert.True(main.Topmost);
            Assert.False(first.Topmost);
            Assert.All(workbench.Layouts.FloatingWindows, window => Assert.True(window.Topmost));
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 恢复布局时浮窗继承主窗口当前的置顶状态，之后继续响应切换。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoredFloatingWindowInheritsCurrentTopmostState(bool topmost)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var file = new WorkspaceLayoutFile
        {
            CurrentLayout = new()
            {
                Main = new()
                {
                    PanelIds = [.. WorkbenchPanelIds.All.Where(id => id != WorkbenchPanelIds.COMPOSER)],
                    ActivePanelId = WorkbenchPanelIds.LYRICS
                },
                FloatingWindows =
                [
                    new()
                    {
                        Layout = new()
                        {
                            PanelIds = [WorkbenchPanelIds.COMPOSER],
                            ActivePanelId = WorkbenchPanelIds.COMPOSER
                        },
                        Bounds = new() { X = 100, Y = 100, Width = 640, Height = 400 }
                    }
                ],
                FocusedPanelId = WorkbenchPanelIds.COMPOSER
            }
        };
        await new WorkspaceLayoutStore(context.ViewModel.ConfigDirectory).SaveAsync(file);
        var main = new MainWindow { DataContext = context.ViewModel, Topmost = topmost };

        try
        {
            main.Show();
            await main.Initialization;
            PumpLayout(main);
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            Assert.Equal(topmost, floating.Topmost);

            main.Topmost = !topmost;

            Assert.Equal(!topmost, floating.Topmost);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 已有和新打开的辅助窗口跟随主窗口，模态与非模态窗口使用同一置顶策略。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuxiliaryWindowsFollowCurrentAndChangedTopmostState(bool modal)
    {
        var main = new Window();
        var windows = new WorkbenchWindowRegistry(main);

        try
        {
            main.Show();
            windows.RegisterBusinessWindow(main);
            var existing = windows.OpenAuxiliary("existing", "已有辅助窗口", () => new TextBlock());
            Assert.False(existing.Topmost);

            main.Topmost = true;

            Assert.True(existing.Topmost);
            var added = windows.OpenAuxiliary("added", "新辅助窗口", () => new TextBlock(), modal, owner: existing);
            Assert.True(added.Topmost);

            main.Topmost = false;

            Assert.False(existing.Topmost);
            Assert.False(added.Topmost);
        }
        finally
        {
            windows.Dispose();
            main.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 独立置顶浮窗打开的辅助窗口及嵌套确认框跟随实际 owner，其他业务窗维持独立状态。
    /// </summary>
    [AvaloniaFact]
    public void AuxiliaryWindowsFollowIndependentlyPinnedOwner()
    {
        var main = new Window();
        var floating = new Window();
        var windows = new WorkbenchWindowRegistry(main);

        try
        {
            main.Show();
            floating.Show(main);
            windows.RegisterBusinessWindow(main);
            windows.RegisterBusinessWindow(floating);
            WorkbenchWindowTopmost.SetIsIndependentlyTopmost(floating, true);
            var auxiliary = windows.OpenAuxiliary("auxiliary", "设置", () => new TextBlock(), owner: floating);
            var nested = windows.OpenAuxiliary("nested", "确认", () => new TextBlock(), true, owner: auxiliary);
            Assert.True(auxiliary.Topmost);
            Assert.True(nested.Topmost);
            Assert.False(main.Topmost);

            WorkbenchWindowTopmost.SetIsIndependentlyTopmost(floating, false);

            Assert.False(auxiliary.Topmost);
            Assert.False(nested.Topmost);

            main.Topmost = true;
            WorkbenchWindowTopmost.SetIsIndependentlyTopmost(floating, true);
            main.Topmost = false;

            Assert.True(floating.Topmost);
            Assert.True(auxiliary.Topmost);
            Assert.True(nested.Topmost);
            Assert.False(main.Topmost);
        }
        finally
        {
            windows.Dispose();
            floating.Close();
            main.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 注销业务窗口和释放注册表后，后续属性变化不再改写已脱离注册的窗口。
    /// </summary>
    [AvaloniaFact]
    public void UnregisterAndDisposeDetachTopmostSubscriptions()
    {
        var main = new Window();
        var floating = new Window();
        var windows = new WorkbenchWindowRegistry(main);

        try
        {
            windows.RegisterBusinessWindow(main);
            windows.RegisterBusinessWindow(floating);
            windows.UnregisterBusinessWindow(floating);
            main.Topmost = true;
            WorkbenchWindowTopmost.SetIsIndependentlyTopmost(floating, true);
            Assert.False(floating.Topmost);

            windows.RegisterBusinessWindow(floating);
            Assert.True(floating.Topmost);
            windows.Dispose();
            WorkbenchWindowTopmost.SetIsIndependentlyTopmost(floating, false);
            main.Topmost = false;
            Assert.True(floating.Topmost);
        }
        finally
        {
            windows.Dispose();
            floating.Close();
            main.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void CaptureFloatingWindow(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_WINDOW_QA_OUTPUT");

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        window.MouseMove(new(-1, -1));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"{name}.png"));
    }

    private static void AssertButtonFitsTitleBar(Button button)
    {
        foreach (var ancestor in button.GetVisualAncestors().OfType<Control>())
        {
            var point = button.TranslatePoint(default, ancestor)!.Value;
            Assert.InRange(point.X, -0.5, ancestor.Bounds.Width - button.Bounds.Width + 0.5);
            Assert.InRange(point.Y, -0.5, ancestor.Bounds.Height - button.Bounds.Height + 0.5);
        }
    }

    private static void ClickPin(Window window)
    {
        var title = window.GetVisualDescendants().OfType<TitleBarView>().Single();
        var button = title.FindControl<Button>("PinButton")!;
        ClickButton(window, button);
    }

    private static void ClickButton(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        PumpLayout(window);
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
