using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证内置工作台在正常与最小窗口尺寸下的真实布局，并可输出渲染预览。
/// </summary>
public class WorkbenchLayoutRenderTests
{
    /// <summary>
    /// 每套预设中的活动业务面板都有可用区域，菜单始终留在固定顶部。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(WorkspaceLayoutPresets.DEFAULT, 1280, 820, false)]
    [InlineData(WorkspaceLayoutPresets.TRANSLATION, 1280, 820, false)]
    [InlineData(WorkspaceLayoutPresets.LYRICS, 1280, 820, false)]
    [InlineData(WorkspaceLayoutPresets.FORWARDING, 1280, 820, false)]
    [InlineData(WorkspaceLayoutPresets.DEFAULT, 980, 640, false)]
    [InlineData(WorkspaceLayoutPresets.TRANSLATION, 980, 640, false)]
    [InlineData(WorkspaceLayoutPresets.LYRICS, 980, 640, false)]
    [InlineData(WorkspaceLayoutPresets.FORWARDING, 980, 640, false)]
    [InlineData(WorkspaceLayoutPresets.DEFAULT, 1280, 820, true)]
    [InlineData(WorkspaceLayoutPresets.TRANSLATION, 1280, 820, true)]
    [InlineData(WorkspaceLayoutPresets.LYRICS, 1280, 820, true)]
    [InlineData(WorkspaceLayoutPresets.FORWARDING, 1280, 820, true)]
    [InlineData(WorkspaceLayoutPresets.DEFAULT, 980, 640, true)]
    [InlineData(WorkspaceLayoutPresets.TRANSLATION, 980, 640, true)]
    [InlineData(WorkspaceLayoutPresets.LYRICS, 980, 640, true)]
    [InlineData(WorkspaceLayoutPresets.FORWARDING, 980, 640, true)]
    public async Task BuiltInLayoutKeepsActivePanelsUsable(string preset, double width, double height, bool dark)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        context.ViewModel.Settings.UseWindowMenuOnMac = true;
        context.ViewModel.Settings.WindowWidth = width;
        context.ViewModel.Settings.WindowHeight = height;
        context.ViewModel.SelectedWorkspace!.SelectedRoom!.InputDraft = "【示例】全局 Dock 工作台输入草稿";
        var main = new MainWindow
        {
            DataContext = context.ViewModel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.ApplyPreset(preset);
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
            var menu = main.FindControl<Menu>("WindowMenu")!;
            Assert.True(menu.IsVisible);
            Assert.True(menu.Bounds.Height > 0);
            var snapshot = workbench.Layouts.Capture();

            foreach (var panelId in ActivePanels(snapshot.Main!))
            {
                var panel = workbench.Layouts.PanelAdapters[panelId].View;
                Assert.True(panel.IsEffectivelyVisible, panelId);
                Assert.True(panel.Bounds.Width >= 120, $"{panelId}: {panel.Bounds}");
                Assert.True(panel.Bounds.Height >= 40, $"{panelId}: {panel.Bounds}");
                var origin = panel.TranslatePoint(default, main);
                Assert.NotNull(origin);
                Assert.InRange(origin.Value.X, 0, main.Bounds.Width - 120);
                Assert.InRange(origin.Value.Y, menu.Bounds.Bottom, main.Bounds.Height - 40);
            }

            var player = workbench.Panels.Player;
            var toolbar = player.GetVisualDescendants().OfType<WrapPanel>().Single();

            foreach (var control in toolbar.Children)
            {
                Assert.InRange(control.Bounds.Right, 0, toolbar.Bounds.Width + 0.5);
                Assert.InRange(control.Bounds.Bottom, 0, toolbar.Bounds.Height + 0.5);
            }

            var video = player.FindControl<Border>("LivePlayerHost")!;
            var videoTop = video.TranslatePoint(default, player)!.Value.Y;
            var toolbarBottom = toolbar.TranslatePoint(new Point(0, toolbar.Bounds.Height), player)!.Value.Y;
            Assert.True(videoTop >= toolbarBottom, $"播放器不能覆盖工具条：{videoTop} < {toolbarBottom}");

            var composer = workbench.Panels.Composer;

            if (composer.IsEffectivelyVisible)
            {
                AssertComposerFits(composer);
            }

            var directory = Environment.GetEnvironmentVariable("YOHUKE_DOCK_QA_OUTPUT");

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                main.MouseMove(new(-1, -1));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                Dispatcher.UIThread.RunJobs();
                using var frame = main.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, $"{preset}-{width}x{height}-{(dark ? "dark" : "light")}.png"));
            }

            if (composer.IsEffectivelyVisible)
            {
                context.ViewModel.SelectedWorkspace!.SelectedRoom!.DraftSendError = "发送失败：" + new string('错', 100);
                context.ViewModel.SelectedWorkspace.SelectedRoom.IsDraftSendFailed = true;
                Dispatcher.UIThread.RunJobs();
                main.UpdateLayout();
                AssertComposerFits(composer);
            }
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    private static IEnumerable<string> ActivePanels(WorkspaceLayoutNode node)
    {
        if (node.Kind == WorkspaceLayoutNodeKind.Tabs)
        {
            yield return node.ActivePanelId!;
            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var panelId in ActivePanels(child))
            {
                yield return panelId;
            }
        }
    }

    private static void AssertComposerFits(DraftComposerPanelView composer)
    {
        var host = composer.GetVisualAncestors().OfType<ToolControl>().First();
        var bottom = composer.TranslatePoint(new Point(0, composer.Bounds.Height), host)!.Value.Y;
        Assert.InRange(bottom, 0, host.Bounds.Height + 0.5);
        Assert.True(composer.Input.Bounds.Height >= 64);
    }
}
