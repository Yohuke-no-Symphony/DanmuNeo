using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.Captures;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>用离线数据验证三个工具面板的窄宽布局、主题和实际渲染。</summary>
public class ToolPanelsRenderTests
{
    /// <summary>每套内置布局在最小主窗中切到新工具时，工具与输入框仍保持在窗口内。</summary>
    [AvaloniaTheory]
    [InlineData(WorkspaceLayoutPresets.DEFAULT, WorkbenchPanelIds.MODERATION)]
    [InlineData(WorkspaceLayoutPresets.DEFAULT, WorkbenchPanelIds.ACCOUNT_MEDALS)]
    [InlineData(WorkspaceLayoutPresets.DEFAULT, WorkbenchPanelIds.LIVE_CAPTURES)]
    [InlineData(WorkspaceLayoutPresets.TRANSLATION, WorkbenchPanelIds.MODERATION)]
    [InlineData(WorkspaceLayoutPresets.TRANSLATION, WorkbenchPanelIds.ACCOUNT_MEDALS)]
    [InlineData(WorkspaceLayoutPresets.TRANSLATION, WorkbenchPanelIds.LIVE_CAPTURES)]
    [InlineData(WorkspaceLayoutPresets.LYRICS, WorkbenchPanelIds.MODERATION)]
    [InlineData(WorkspaceLayoutPresets.LYRICS, WorkbenchPanelIds.ACCOUNT_MEDALS)]
    [InlineData(WorkspaceLayoutPresets.LYRICS, WorkbenchPanelIds.LIVE_CAPTURES)]
    [InlineData(WorkspaceLayoutPresets.FORWARDING, WorkbenchPanelIds.MODERATION)]
    [InlineData(WorkspaceLayoutPresets.FORWARDING, WorkbenchPanelIds.ACCOUNT_MEDALS)]
    [InlineData(WorkspaceLayoutPresets.FORWARDING, WorkbenchPanelIds.LIVE_CAPTURES)]
    public async Task ToolsFitMinimumWindowInEveryBuiltInPreset(string presetId, string panelId)
    {
        using var fixture = new CaptureWorkbenchTestContext();
        fixture.Application.Settings.WindowWidth = 980;
        fixture.Application.Settings.WindowHeight = 640;
        fixture.Application.Settings.UseWindowMenuOnMac = true;
        var main = new MainWindow { DataContext = fixture.Application };
        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.ApplyPreset(presetId);
            workbench.Layouts.ShowPanel(panelId);
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
            var panel = workbench.Panels.Panels[panelId];
            Assert.True(panel.IsEffectivelyVisible);
            var bottomRight = panel.TranslatePoint(new(panel.Bounds.Width, panel.Bounds.Height), main)!.Value;
            Assert.InRange(bottomRight.X, 0, main.Bounds.Width + 0.5);
            Assert.InRange(bottomRight.Y, 0, main.Bounds.Height + 0.5);
            var tool = Assert.Single(panel.GetVisualAncestors().OfType<ToolControl>());
            var contentOrigin = panel.TranslatePoint(default, tool)!.Value;
            var contentRight = panel.TranslatePoint(new(panel.Bounds.Width, 0), tool)!.Value.X;
            Assert.InRange(contentOrigin.X, 0, tool.Bounds.Width);
            Assert.InRange(contentRight, 0, tool.Bounds.Width + 0.5);
            Assert.InRange(workbench.Layouts.RootDock.MinHeight, 0, workbench.Layouts.Host.Bounds.Height);
            Assert.True(workbench.Panels.Composer.Input.Bounds.Height >= 64);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>工具面板沿用工作台样式，在窄宽和浅深主题下可浏览操作且无折叠外框。</summary>
    [AvaloniaTheory]
    [InlineData(WorkbenchPanelIds.MODERATION, 320, false)]
    [InlineData(WorkbenchPanelIds.MODERATION, 640, false)]
    [InlineData(WorkbenchPanelIds.MODERATION, 320, true)]
    [InlineData(WorkbenchPanelIds.MODERATION, 640, true)]
    [InlineData(WorkbenchPanelIds.ACCOUNT_MEDALS, 320, false)]
    [InlineData(WorkbenchPanelIds.ACCOUNT_MEDALS, 640, false)]
    [InlineData(WorkbenchPanelIds.ACCOUNT_MEDALS, 320, true)]
    [InlineData(WorkbenchPanelIds.ACCOUNT_MEDALS, 640, true)]
    [InlineData(WorkbenchPanelIds.LIVE_CAPTURES, 320, false)]
    [InlineData(WorkbenchPanelIds.LIVE_CAPTURES, 240, false)]
    [InlineData(WorkbenchPanelIds.LIVE_CAPTURES, 640, false)]
    [InlineData(WorkbenchPanelIds.LIVE_CAPTURES, 320, true)]
    [InlineData(WorkbenchPanelIds.LIVE_CAPTURES, 240, true)]
    [InlineData(WorkbenchPanelIds.LIVE_CAPTURES, 640, true)]
    public async Task ToolPanelsFitSharedThemeAtNarrowAndWideWidths(string panelId, int width, bool dark)
    {
        using var fixture = new CaptureWorkbenchTestContext();
        fixture.Moderation.ListResponseAsync = (roomId, _, _) => Task.FromResult<IReadOnlyList<ModerationRecord>>(
        [
            new() { RecordId = 1, RoomId = roomId, UserUid = 30001, UserName = "需要管理的用户", CreatedAtText = "2026-10-19 13:04:05", ExecutorName = "离线测试房管" },
            new() { RecordId = 2, RoomId = roomId, UserUid = 30002, UserName = "另一个较长的用户名称", CreatedAtText = "2026-10-19 13:06:07", ExecutorName = "离线测试房管" }
        ]);
        await fixture.Application.StartToolsAsync();
        await fixture.Application.GalleryInitialization;
        await fixture.Application.LiveCaptures.CaptureAsync(CreatePreviewFrame(), "离线演示房间");
        fixture.Application.LiveCaptures.Captures[0].Note = "第一段同传结束，稍后核对字幕。\n备注独立保存，导出时附在画面下方。";
        await fixture.Application.LiveCaptures.FlushAsync();
        using var context = new WorkbenchContext(fixture.Application);
        using var panels = new WorkbenchPanelSet(context);
        var panel = panels.Panels[panelId];
        var window = new Window
        {
            Width = width, Height = 640, Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            if (panelId == WorkbenchPanelIds.MODERATION)
            {
                await fixture.Application.ModerationManager.ActivateAsync();
            }
            else if (panelId == WorkbenchPanelIds.ACCOUNT_MEDALS)
            {
                await fixture.Application.AccountMedals.ActivateAsync();
            }
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(panel.IsEnabled);
            Assert.True(panel.IsEffectivelyVisible);
            Assert.InRange(panel.Bounds.Width, 240, width);
            Assert.DoesNotContain(panel.GetVisualDescendants(), visual => visual is Expander);
            foreach (var toolbar in panel.GetVisualDescendants().OfType<WrapPanel>())
            {
                foreach (var child in toolbar.Children.Where(child => child.IsVisible))
                {
                    Assert.InRange(child.Bounds.Right, 0, toolbar.Bounds.Width + 1);
                }
            }

            var directory = Environment.GetEnvironmentVariable("YOHUKE_TOOLS_QA_OUTPUT");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                window.MouseMove(new(-1, -1));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                Dispatcher.UIThread.RunJobs();
                using var rendered = window.CaptureRenderedFrame();
                Assert.NotNull(rendered);
                rendered.Save(Path.Combine(directory, $"{panelId}-{width}-{(dark ? "dark" : "light")}.png"));
            }
        }
        finally
        {
            window.Close();
            await fixture.Application.LiveCaptures.FlushAsync();
        }
    }

    private static LiveCapturedFrame CreatePreviewFrame()
    {
        var sample = new Border
        {
            Width = 640, Height = 360, Background = new SolidColorBrush(Color.Parse("#203650")),
            Child = new TextBlock
            {
                Text = "离线画面\n13:04:05  ·  直播打点", FontSize = 30, Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        sample.Measure(new(640, 360));
        sample.Arrange(new(0, 0, 640, 360));
        using var bitmap = new RenderTargetBitmap(new(640, 360), new(96, 96));
        bitmap.Render(sample);
        using var output = new MemoryStream();
        bitmap.Save(output);
        var capturedAt = new DateTimeOffset(2026, 10, 19, 13, 4, 5, TimeSpan.FromHours(8));
        return new()
        {
            PngBytes = output.ToArray(), RoomId = "123456", BroadcastSessionId = "123456:offline",
            StartedAt = capturedAt.AddHours(-2), CapturedAt = capturedAt, Width = 640, Height = 360
        };
    }
}
