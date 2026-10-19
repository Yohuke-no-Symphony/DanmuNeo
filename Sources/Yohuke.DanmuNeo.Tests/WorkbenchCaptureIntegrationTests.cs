using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.Captures;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证截图的键盘路由、来源隔离和退出保存屏障。</summary>
public class WorkbenchCaptureIntegrationTests
{
    /// <summary>退出取消非破坏性批量导出后，仍保存最新备注并完成关闭。</summary>
    [AvaloniaFact]
    public async Task ShutdownCancelsLongExportAndPersistsLatestNote()
    {
        var exporter = new BlockingLiveCaptureExporter();
        using var fixture = new CaptureWorkbenchTestContext(exporter: exporter);
        var main = new MainWindow { DataContext = fixture.Application };
        try
        {
            main.Show();
            await main.Initialization;
            await fixture.Application.GalleryInitialization;
            var gallery = fixture.Application.LiveCaptures;
            await gallery.CaptureAsync(LiveCaptureStoreTests.CreateFrame(), "room");
            var export = gallery.ExportAsync(gallery.Captures, "offline");
            await exporter.Started.Task;
            gallery.Captures[0].Note = "导出中编辑的最终备注";
            var closing = main.CloseWorkbenchAsync();
            try
            {
                await closing.WaitAsync(TimeSpan.FromSeconds(3));
            }
            finally
            {
                exporter.Release.TrySetResult();
                await closing;
                await export;
            }
            Assert.False(main.IsVisible);
            Assert.Equal("导出中编辑的最终备注", Assert.Single(fixture.Store.Records).Note);
            Assert.False(fixture.Store.HasPendingChanges);
        }
        finally
        {
            exporter.Release.TrySetResult();
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>从玩家抓图保存失败时，日志与当前状态给出重试入口，不依赖图库已打开。</summary>
    [AvaloniaFact]
    public async Task FailedFrameSaveShowsRetryGalleryAndApplicationError()
    {
        using var fixture = new CaptureWorkbenchTestContext();
        var main = new MainWindow { DataContext = fixture.Application };
        try
        {
            main.Show();
            await main.Initialization;
            await fixture.Application.GalleryInitialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            fixture.EnablePlayer();
            Dispatcher.UIThread.RunJobs();
            fixture.Store.FailWrites = true;
            await workbench.Panels.CaptureCurrentFrameAsync();
            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();

            Assert.True(fixture.Store.HasPendingOriginals);
            Assert.False(fixture.Application.LiveCaptures.CanCapture);
            Assert.Contains(fixture.Application.Log.Entries, entry => entry.IsError && entry.Source == "直播打点截图");
            Assert.Contains("重试", fixture.Application.StatusMessage);
            Assert.True(workbench.Panels.LiveCaptures.IsEffectivelyVisible);
        }
        finally
        {
            fixture.Store.FailWrites = false;
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>播放器宿主卸载时暂停图库抓图，恢复挂载后重新开放。</summary>
    [AvaloniaFact]
    public async Task CaptureAvailabilityFollowsTheActualNativeHost()
    {
        using var fixture = new CaptureWorkbenchTestContext();
        using var context = new WorkbenchContext(fixture.Application);
        using var panels = new WorkbenchPanelSet(context);
        await fixture.Application.StartToolsAsync();
        await fixture.Application.GalleryInitialization;
        fixture.EnablePlayer();
        Assert.False(fixture.Application.LiveCaptures.CanCapture);
        var window = new Window { Content = panels.Player };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Application.LiveCaptures.CanCapture);
            window.Content = null;
            Assert.True(fixture.Room.IsLivePlayerVisible);
            Assert.False(fixture.Application.LiveCaptures.CanCapture);
            window.Content = panels.Player;
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Application.LiveCaptures.CanCapture);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>默认和改绑快捷键在输入框生效，按住不重复，原生焦点服从相同停用及交互门禁。</summary>
    [AvaloniaFact]
    public async Task CaptureShortcutWorksInDraftAndSharesNativeInteractionGate()
    {
        using var fixture = new CaptureWorkbenchTestContext();
        Assert.Equal(0, fixture.Store.LoadCount);
        var main = new MainWindow { DataContext = fixture.Application };
        try
        {
            main.Show();
            await main.Initialization;
            await fixture.Application.GalleryInitialization;
            Assert.Equal(1, fixture.Store.LoadCount);
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            var token = fixture.EnablePlayer();
            workbench.Panels.SetCaptureNoteEditor((_, _) => Task.FromResult<string?>(null));
            Dispatcher.UIThread.RunJobs();
            fixture.Room.InputDraft = "保留草稿";
            workbench.Panels.FocusInputDraft();
            Dispatcher.UIThread.RunJobs();
            main.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            main.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            main.KeyReleaseQwerty(PhysicalKey.P, RawInputModifiers.Alt);
            await fixture.Application.LiveCaptures.FlushAsync();
            Assert.Equal(1, fixture.Frames.RequestCount);
            var first = Assert.Single(fixture.Store.Records);
            Assert.Equal("123456", first.RoomId);
            Assert.Equal("本地房间名称", first.RoomName);
            Assert.Equal("保留草稿", fixture.Room.InputDraft);

            workbench.SetShutdownPending(true);
            fixture.Frames.RequestShortcut(token);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, fixture.Frames.RequestCount);
            workbench.SetShutdownPending(false);
            var binding = fixture.Application.Settings.ShortcutBindings.Single(item => item.ActionKey == ShortcutActionKeys.LIVE_PLAYER_CAPTURE);
            binding.IsEnabled = false;
            fixture.Frames.RequestShortcut(token);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, fixture.Frames.RequestCount);
            binding.GestureText = "Alt+O";
            binding.IsEnabled = true;
            main.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.Alt);
            main.KeyReleaseQwerty(PhysicalKey.O, RawInputModifiers.Alt);
            await fixture.Application.LiveCaptures.FlushAsync();
            Assert.Equal(2, fixture.Frames.RequestCount);
            fixture.Frames.RequestShortcut(token);
            Dispatcher.UIThread.RunJobs();
            await fixture.Application.LiveCaptures.FlushAsync();
            Assert.Equal(3, fixture.Frames.RequestCount);
            Assert.Equal(3, fixture.Store.Records.Count);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>图库写入失败时保留主窗和浮窗，修复后重试保存最新备注再退出。</summary>
    [AvaloniaFact]
    public async Task FailedGalleryFlushKeepsWorkbenchRetryable()
    {
        using var fixture = new CaptureWorkbenchTestContext();
        var main = new MainWindow { DataContext = fixture.Application };
        try
        {
            main.Show();
            await main.Initialization;
            await fixture.Application.GalleryInitialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.LIVE_CAPTURES);
            Dispatcher.UIThread.RunJobs();
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            await fixture.Application.LiveCaptures.CaptureAsync(LiveCaptureStoreTests.CreateFrame(), "room");
            fixture.Store.FailWrites = true;
            fixture.Application.LiveCaptures.Captures[0].Note = "退出前备注";

            await main.CloseWorkbenchAsync();

            Assert.True(main.IsVisible);
            Assert.True(main.IsEnabled);
            Assert.True(floating.IsVisible);
            Assert.Same(floating, Assert.Single(workbench.Layouts.FloatingWindows));
            Assert.False(workbench.Windows.IsShutdownPending);
            Assert.True(fixture.Application.LiveCaptures.HasPendingChanges);
            fixture.Application.LiveCaptures.Captures[0].Note = "修复后保存的最终备注";
            fixture.Store.FailWrites = false;
            await main.CloseWorkbenchAsync();

            Assert.False(main.IsVisible);
            Assert.False(floating.IsVisible);
            var restored = await fixture.Store.LoadAsync();
            Assert.Equal("修复后保存的最终备注", Assert.Single(restored).Note);
        }
        finally
        {
            fixture.Store.FailWrites = false;
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>切空当前选择后拒绝迟到原帧，图库面板仍可浏览。</summary>
    [AvaloniaFact]
    public async Task LateFrameCannotEnterGalleryAfterSelectionChanges()
    {
        var frame = LiveCaptureStoreTests.CreateFrame("123456");
        var completion = new TaskCompletionSource<LiveCapturedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new CaptureWorkbenchTestContext(new(frame) { DeferredFrame = completion });
        var main = new MainWindow { DataContext = fixture.Application };
        try
        {
            main.Show();
            await main.Initialization;
            await fixture.Application.GalleryInitialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            fixture.EnablePlayer();
            Dispatcher.UIThread.RunJobs();
            var capture = workbench.Panels.CaptureCurrentFrameAsync();
            await fixture.Frames.RequestStarted.Task;
            fixture.Application.SelectedWorkspace = null;
            completion.SetResult(frame);
            await capture;
            Assert.Empty(fixture.Store.Records);
            Assert.True(workbench.Panels.LiveCaptures.IsEnabled);
            Assert.Same(fixture.Application.LiveCaptures, workbench.Panels.LiveCaptures.DataContext);
            Assert.False(fixture.Application.LiveCaptures.CanCapture);
        }
        finally
        {
            completion.TrySetResult(frame);
            await main.CloseWorkbenchAsync();
        }
    }
}
