using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证记录文件名随当前房间、文件操作和界面语言更新 Dock 与浮窗标题。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class LiveMarkerPanelPresentationTests
{
    /// <summary>标题跟随文件会话，旧房间的后续操作不改变当前标题，关闭文件后恢复通用标题。</summary>
    [AvaloniaFact]
    public async Task PanelAndFloatingTitlesFollowCurrentFileRoomAndLanguage()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        await using var fixture = new LiveMarkerApplicationTestContext();
        var main = new Window { Width = 1200, Height = 800 };
        var menu = new Menu();
        var workbench = await WorkbenchSession.CreateAsync(main, menu, fixture.Application, supportsNativeMenus: false);
        DockPanel.SetDock(menu, Avalonia.Controls.Dock.Top);
        main.Content = new DockPanel { Children = { menu, workbench.Layouts.Host } };
        main.Show();

        try
        {
            var panel = workbench.Layouts.PanelAdapters[WorkbenchPanelIds.LIVE_MARKER];
            var first = fixture.FirstRoom.LiveMarker;
            var second = fixture.SecondRoom.LiveMarker;
            Assert.Equal("直播记录", panel.Title);
            await first.NewAsync(Path.Combine(fixture.DirectoryPath, "新建记录.yaml"));
            Assert.Equal("直播记录（新建记录.yaml）", panel.Title);
            first.DraftText = "文件名随当前记录文件更新";
            first.AddMarkerCommand.Execute(null);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.LIVE_MARKER);
            Pump(main);
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            Assert.Equal("直播记录（新建记录.yaml） — DanmuNeo", floating.Title);

            await first.SaveAsAsync(Path.Combine(fixture.DirectoryPath, "高光记录.yaml"));
            Pump(floating);
            Assert.Equal("直播记录（高光记录.yaml）", panel.Title);
            Assert.Equal("直播记录（高光记录.yaml） — DanmuNeo", floating.Title);
            Assert.DoesNotContain(workbench.Panels.LiveMarker.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "高光记录.yaml");
            SaveFrame(floating);

            fixture.Workspace.SelectedRoom = fixture.SecondRoom;
            Assert.Equal("直播记录", panel.Title);
            var secondPath = Path.Combine(fixture.DirectoryPath, "第二房间.yaml");
            fixture.Files.Documents[secondPath] = new() { StartTime = fixture.Time.GetLocalNow() };
            await second.OpenAsync(secondPath);
            Assert.Equal("直播记录（第二房间.yaml）", panel.Title);
            await first.SaveAsAsync(Path.Combine(fixture.DirectoryPath, "第一个房间另存.yaml"));
            Assert.Equal("直播记录（第二房间.yaml）", panel.Title);
            fixture.Workspace.SelectedRoom = fixture.FirstRoom;
            Assert.Equal("直播记录（第一个房间另存.yaml）", panel.Title);

            Localization.SetLanguage("en-US");
            Pump(floating);
            Assert.Equal("Live markers (第一个房间另存.yaml)", panel.Title);
            Assert.Equal("Live markers (第一个房间另存.yaml) — DanmuNeo", floating.Title);
            await first.CloseAsync();
            Assert.Equal("Live markers", panel.Title);
            Assert.Equal("Live markers — DanmuNeo", floating.Title);
            fixture.Workspace.SelectedRoom = fixture.SecondRoom;
            Assert.Equal("Live markers (第二房间.yaml)", panel.Title);
            fixture.Application.SelectedWorkspace = null;
            Assert.Equal("Live markers", panel.Title);
        }
        finally
        {
            await workbench.DisposeAsync();
            main.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void SaveFrame(Window window)
    {
        var directory = Environment.GetEnvironmentVariable("DANMUNEO_MARKER_SCREENSHOT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        window.Width = 1000;
        window.Height = 600;
        window.RequestedThemeVariant = ThemeVariant.Dark;
        window.MouseMove(new(-1, -1));
        Pump(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, "live-marker-floating-title.png"));
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
