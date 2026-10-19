using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证独立图库的筛选、自动保存与可抓图状态。</summary>
public sealed class LiveCapturesViewModelTests
{
    /// <summary>忙态导出拒绝重复操作，退出保存等待当前操作且不能提前恢复可截图状态。</summary>
    [AvaloniaFact]
    public async Task OverlappingExportCannotReleaseAnotherOperationsBusyState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "danmu-gallery-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LiveCaptureStore(directory);
            var exporter = new BlockingLiveCaptureExporter();
            using var vm = new LiveCapturesViewModel(store, exporter);
            vm.SetCaptureSourceAvailable(true);
            await vm.CaptureAsync(LiveCaptureStoreTests.CreateFrame(), "room");
            vm.SelectedCaptures.Add(vm.Captures[0]);
            Assert.True(vm.CanCapture);
            var first = vm.ExportAsync(vm.Captures, "offline");
            await exporter.Started.Task;
            Assert.True(vm.IsBusy);
            Assert.False(vm.CanCapture);
            Assert.False(vm.CanExportSingle);
            Assert.False(vm.CanExportSelected);
            Assert.False(vm.CanExportFiltered);
            await vm.ExportAsync(vm.Captures, "offline").WaitAsync(TimeSpan.FromMilliseconds(500));
            await vm.ExportToFileAsync(vm.Captures[0], "offline.png").WaitAsync(TimeSpan.FromMilliseconds(500));
            await vm.SaveAsync().WaitAsync(TimeSpan.FromMilliseconds(500));
            Assert.Equal(1, exporter.Calls);
            Assert.True(vm.IsBusy);
            var closingFlush = vm.FlushAsync();
            Assert.False(closingFlush.IsCompleted);
            exporter.Release.TrySetResult();
            await first;
            await closingFlush;
            Assert.False(vm.IsBusy);
            Assert.True(vm.CanCapture);
            Assert.True(vm.CanExportSelected);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>备注防抖保存且截图能力由组合根明确同步。</summary>
    [AvaloniaFact]
    public async Task NotesAutoSaveAndCaptureAvailabilityIsExplicit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "danmu-gallery-notes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LiveCaptureStore(directory);
            using var vm = new LiveCapturesViewModel(store, new LiveCaptureExportService(store), TimeSpan.FromMilliseconds(10));
            Assert.False(vm.CanCapture);
            vm.SetCaptureSourceAvailable(true);
            Assert.True(vm.CanCapture);
            vm.IsCapturing = true;
            Assert.False(vm.CanCapture);
            await vm.CaptureAsync(LiveCaptureStoreTests.CreateFrame(), "room");
            vm.IsCapturing = false;
            var capture = Assert.Single(vm.Captures);
            capture.Note = "automatically saved";
            await vm.PendingNoteSave;
            Assert.False(vm.HasPendingChanges);
            Assert.Equal("automatically saved", Assert.Single(await new LiveCaptureStore(directory).LoadAsync()).Note);
            capture.Note = "last edit before closing";
            await vm.FlushAsync();
            Assert.Equal(Yohuke.DanmuNeo.I18n.Localization.Get("Captures.Saved"), vm.StatusText);
            Assert.Equal("last edit before closing", Assert.Single(await new LiveCaptureStore(directory).LoadAsync()).Note);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>原图保存失败时暂停新增截图直到重试成功。</summary>
    [AvaloniaFact]
    public async Task FailedOriginalSaveDisablesNewCapturesUntilRetrySucceeds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "danmu-gallery-failure-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(directory, "blocking directory");
            var store = new LiveCaptureStore(directory);
            using var vm = new LiveCapturesViewModel(store, new LiveCaptureExportService(store));
            vm.SetCaptureSourceAvailable(true);
            await vm.CaptureAsync(LiveCaptureStoreTests.CreateFrame(), "room");
            Assert.False(vm.CanCapture);
            Assert.Single(vm.Captures);
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CaptureAsync(LiveCaptureStoreTests.CreateFrame(), "room"));
            File.Delete(directory);
            await vm.FlushAsync();
            Assert.True(vm.CanCapture);
            Assert.Single(vm.Captures);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
            if (File.Exists(directory))
            {
                File.Delete(directory);
            }
        }
    }

    /// <summary>图库跨房间保留记录和备注并支持场次筛选。</summary>
    [AvaloniaFact]
    public async Task GalleryKeepsAllRoomsAndNotesWithoutCurrentRoomAndFiltersSessions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "danmu-gallery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LiveCaptureStore(directory);
            using var vm = new LiveCapturesViewModel(store, new LiveCaptureExportService(store));
            await vm.InitializeAsync();
            await vm.CaptureAsync(LiveCaptureStoreTests.CreateFrame("one", 1000), "one");
            await vm.CaptureAsync(LiveCaptureStoreTests.CreateFrame("one", 2000), "one");
            await vm.CaptureAsync(LiveCaptureStoreTests.CreateFrame("two", 2000), "two");
            Assert.Equal(3, vm.FilteredCaptures.Count);
            vm.SelectedRoom = vm.RoomOptions.Single(option => option.Key == "one");
            Assert.Equal(2, vm.FilteredCaptures.Count);
            vm.SelectedSession = vm.SessionOptions.Single(option => option.Key == "one:1000");
            var capture = Assert.Single(vm.FilteredCaptures);
            capture.Note = "edited after capture";
            await vm.FlushAsync();
            vm.ClearFilters();
            Assert.Equal(3, vm.FilteredCaptures.Count);
            Assert.Equal("edited after capture", (await new LiveCaptureStore(directory).LoadAsync()).Single(record => record.Id == capture.Id).Note);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
