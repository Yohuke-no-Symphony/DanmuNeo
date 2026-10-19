using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证本地房间短号与取流真实房号不同的原帧归属。</summary>
public sealed class LivePlayerCaptureAliasTests
{
    /// <summary>即使抓帧依赖迟到返回，切房后也不能交付给图库。</summary>
    [AvaloniaFact]
    public async Task LateFrameIsRejectedAfterRoomSelectionChanges()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        var token = Guid.NewGuid().ToString("N");
        fixture.Room.LivePlayerUrl = "http://127.0.0.1:1/player?token=" + token;
        fixture.Room.IsLivePlayerVisible = true;
        using var context = new WorkbenchContext(fixture.ViewModel);
        var frame = LiveCaptureStoreTests.CreateFrame("123456");
        var completion = new TaskCompletionSource<Yohuke.DanmuNeo.Models.Captures.LiveCapturedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captureService = new FakeLiveFrameCaptureService(frame) { DeferredFrame = completion };
        using var player = new LivePlayerPanelView(context, captureService);
        var window = new Window { Content = player };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var pending = player.CaptureFrameAsync();
            await captureService.RequestStarted.Task;
            fixture.ViewModel.SelectedWorkspace!.SelectedRoom = null;
            completion.TrySetResult(frame);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>原生回调在 UI 中复核来源，切房及提前释放后不能触发旧截图动作。</summary>
    [AvaloniaFact]
    public void NativeWorkerShortcutCannotCrossRoomSelectionOrDisposedHost()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        var token = Guid.NewGuid().ToString("N");
        fixture.Room.LivePlayerUrl = "http://127.0.0.1:1/player?token=" + token;
        fixture.Room.IsLivePlayerVisible = true;
        using var context = new WorkbenchContext(fixture.ViewModel);
        var captureService = new FakeLiveFrameCaptureService(LiveCaptureStoreTests.CreateFrame());
        using var player = new LivePlayerPanelView(context, captureService);
        var window = new Window { Content = player };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            player.SetCaptureGesture("Alt+P");
            var actions = 0;
            player.CaptureRequested += (_, _) => actions++;
            Task.Run(() => captureService.RequestShortcut(token)).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, actions);
            Task.Run(() => captureService.RequestShortcut(token)).GetAwaiter().GetResult();
            fixture.ViewModel.SelectedWorkspace!.SelectedRoom = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, actions);
            player.Dispose();
            window.Content = null;
            Task.Run(() => captureService.RequestShortcut(token)).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, actions);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>短号选房可保存真实房号的原帧并保留抓图时的本地房名。</summary>
    [AvaloniaFact]
    public async Task AliasRoomCanCaptureCanonicalSourceRoom()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.Room.RoomId = "6";
        fixture.Room.RoomName = "local alias name";
        var token = Guid.NewGuid().ToString("N");
        fixture.Room.LivePlayerUrl = "http://127.0.0.1:1/player?token=" + token;
        fixture.Room.IsLivePlayerVisible = true;
        using var context = new WorkbenchContext(fixture.ViewModel);
        var frame = LiveCaptureStoreTests.CreateFrame("123456");
        var captureService = new FakeLiveFrameCaptureService(frame);
        using var player = new LivePlayerPanelView(context, captureService);
        var window = new Window { Width = 640, Height = 400, Content = player };
        var directory = Path.Combine(Path.GetTempPath(), "danmu-alias-capture-" + Guid.NewGuid().ToString("N"));
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var captured = await player.CaptureFrameAsync();
            var store = new LiveCaptureStore(directory);
            var saved = store.Add(captured, fixture.Room.RoomName);
            await store.FlushAsync();
            Assert.Equal("123456", saved.RoomId);
            Assert.Equal("local alias name", saved.RoomName);
        }
        finally
        {
            window.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
