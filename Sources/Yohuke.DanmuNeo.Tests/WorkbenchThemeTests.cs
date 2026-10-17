using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证 AegiNext 风格的工作台主题在主窗、浮窗和辅助窗间即时同步。
/// </summary>
public class WorkbenchThemeTests
{
    /// <summary>
    /// 切换浅深主题不重建面板，Dock 表面、活动标题和紧凑按钮应用统一资源。
    /// </summary>
    [AvaloniaFact]
    public async Task ThemeSwitchUpdatesAllHostsAndKeepsPanelInstances()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var previousTheme = Application.Current!.RequestedThemeVariant;
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            var panels = workbench.Panels.Panels.ToDictionary();
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            workbench.MenuCatalog.SettingsCommand.Execute(null);
            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("settings"));

            foreach (var mode in new[] { AppThemeMode.Light, AppThemeMode.Dark })
            {
                await context.ViewModel.SelectThemeModeAsync(context.ViewModel.ThemeModeOptions.Single(option => option.Value == mode));
                Dispatcher.UIThread.RunJobs();
                var variant = mode == AppThemeMode.Light ? ThemeVariant.Light : ThemeVariant.Dark;
                var background = Color.Parse(mode == AppThemeMode.Light ? "#F2F4F8" : "#12151C");
                var surface = Color.Parse(mode == AppThemeMode.Light ? "#FFFFFF" : "#1C222D");

                foreach (var host in new[] { main, floating, settings })
                {
                    host.UpdateLayout();
                    Assert.Equal(variant, host.ActualThemeVariant);
                    Assert.Equal(background, Assert.IsAssignableFrom<ISolidColorBrush>(host.Background).Color);
                    var titleBar = Assert.Single(host.GetVisualDescendants().OfType<WindowTitleBar>());
                    var titleSurface = titleBar.FindControl<Border>("TitleBarSurface")!;
                    Assert.Equal(background, Assert.IsAssignableFrom<ISolidColorBrush>(titleSurface.Background).Color);
                }

                var frames = main.GetVisualDescendants().Concat(floating.GetVisualDescendants())
                    .OfType<Border>().Where(border => border.Classes.Contains("workbenchFrame")).ToArray();
                Assert.NotEmpty(frames);
                Assert.All(frames, frame =>
                {
                    Assert.Equal(surface, Assert.IsAssignableFrom<ISolidColorBrush>(frame.Background).Color);
                    Assert.Equal(new CornerRadius(10), frame.CornerRadius);
                    Assert.True(frame.ClipToBounds);
                });

                var chrome = floating.GetVisualDescendants().OfType<ToolChromeControl>().Single();
                var grip = chrome.GetVisualDescendants().OfType<Grid>().Single(grid => grid.Name == "PART_Grip");
                Assert.Equal(surface, Assert.IsAssignableFrom<ISolidColorBrush>(grip.Background).Color);
                var buttons = chrome.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("dockChromeButton")).ToArray();
                Assert.NotEmpty(buttons);
                Assert.All(buttons, button =>
                {
                    Assert.Equal(22, button.Width);
                    Assert.Equal(22, button.MinHeight);
                });
                Assert.All(panels, pair => Assert.Same(pair.Value, workbench.Panels.Panels[pair.Key]));
            }
        }
        finally
        {
            await main.CloseWorkbenchAsync();
            Application.Current!.RequestedThemeVariant = previousTheme;
        }
    }
}
