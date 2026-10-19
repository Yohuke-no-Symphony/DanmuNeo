using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证截图备注的确认、跳过、持久化失败和退出生命周期。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class CaptureNoteDialogTests
{
    /// <summary>原图在弹窗前落盘，仅保存确认写入草稿；其他关闭方式保留无备注截图。</summary>
    [AvaloniaTheory]
    [InlineData("save")]
    [InlineData("skip")]
    [InlineData("close")]
    [InlineData("escape")]
    public async Task CapturePersistsBeforePromptAndOnlyConfirmedDraftIsApplied(string action)
    {
        using var fixture = new CaptureWorkbenchTestContext();
        var main = new MainWindow { DataContext = fixture.Application };
        try
        {
            var workbench = await InitializeAsync(main, fixture);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.LIVE_PLAYER);
            Dispatcher.UIThread.RunJobs();
            var player = Assert.Single(workbench.Layouts.FloatingWindows);
            WorkbenchWindowTopmost.SetIsIndependentlyTopmost(player, true);
            player.Activate();
            var capture = workbench.Panels.CaptureCurrentFrameAsync();
            var dialog = await WaitForNoteAsync(workbench);
            var record = Assert.Single(fixture.Store.Records);
            Assert.True(File.Exists(fixture.Store.GetOriginalPath(record.Id)));
            Assert.False(fixture.Store.HasPendingChanges);
            Assert.True(fixture.Application.LiveCaptures.IsCapturing);
            Assert.False(capture.IsCompleted);
            Assert.Same(player, dialog.Owner);
            Assert.True(dialog.Topmost);
            Assert.False(main.IsEnabled);
            var view = dialog.GetVisualDescendants().OfType<CaptureNoteDialogView>().Single();
            view.FindControl<TextBox>("NoteInput")!.Text = "本次打点备注\n第二行";
            Assert.Equal("", fixture.Application.LiveCaptures.Captures[0].Note);
            await workbench.Panels.CaptureCurrentFrameAsync();
            Assert.Equal(1, fixture.Frames.RequestCount);
            if (action == "save")
            {
                view.FindControl<Button>("SaveNoteButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else if (action == "skip")
            {
                view.FindControl<Button>("SkipNoteButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else if (action == "escape")
            {
                dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            }
            else
            {
                dialog.Close();
            }
            await capture.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(dialog.IsVisible);
            Assert.False(workbench.Windows.IsModal);
            Assert.False(fixture.Application.LiveCaptures.IsCapturing);
            Assert.True(main.IsEnabled);
            Assert.False(fixture.Store.HasPendingChanges);
            Assert.Equal(action == "save" ? "本次打点备注\n第二行" : "",
                Assert.Single(await fixture.Store.LoadAsync()).Note);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>已有房间筛选隐藏新截图时，备注仍归属本次截图而非旧选中项。</summary>
    [AvaloniaFact]
    public async Task PromptTargetsTheNewCaptureEvenWhenItIsFilteredOut()
    {
        using var fixture = new CaptureWorkbenchTestContext();
        var main = new MainWindow { DataContext = fixture.Application };
        try
        {
            var workbench = await InitializeAsync(main, fixture);
            var gallery = fixture.Application.LiveCaptures;
            var old = await gallery.CaptureAsync(LiveCaptureStoreTests.CreateFrame("999"), "筛选中的旧房间");
            gallery.SelectedRoom = gallery.RoomOptions.Single(option => option.Key == "999");
            var capture = workbench.Panels.CaptureCurrentFrameAsync();
            var dialog = await WaitForNoteAsync(workbench);
            Assert.Same(old, gallery.SelectedCapture);
            var view = dialog.GetVisualDescendants().OfType<CaptureNoteDialogView>().Single();
            view.FindControl<TextBox>("NoteInput")!.Text = "新截图备注";
            view.FindControl<Button>("SaveNoteButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await capture.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("", old.Note);
            Assert.Equal("新截图备注", fixture.Store.Records.Single(record => record.RoomId == "123456").Note);
            Assert.Single(gallery.FilteredCaptures);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>备注落盘失败时保留原图及草稿，打开图库重试且不伪报抓帧失败。</summary>
    [AvaloniaFact]
    public async Task FailedNoteSaveExposesRetryWithoutLosingTheOriginal()
    {
        using var fixture = new CaptureWorkbenchTestContext();
        var main = new MainWindow { DataContext = fixture.Application };
        try
        {
            var workbench = await InitializeAsync(main, fixture);
            var capture = workbench.Panels.CaptureCurrentFrameAsync();
            var dialog = await WaitForNoteAsync(workbench);
            var view = dialog.GetVisualDescendants().OfType<CaptureNoteDialogView>().Single();
            view.FindControl<TextBox>("NoteInput")!.Text = "待重试备注";
            fixture.Store.FailWrites = true;
            view.FindControl<Button>("SaveNoteButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await capture.WaitAsync(TimeSpan.FromSeconds(5));
            var gallery = fixture.Application.LiveCaptures;
            Assert.True(gallery.HasPendingChanges);
            Assert.False(gallery.HasPendingOriginals);
            Assert.Contains("备注", fixture.Application.StatusMessage);
            Assert.True(workbench.Panels.LiveCaptures.IsEffectivelyVisible);
            Assert.Equal("待重试备注", Assert.Single(fixture.Store.Records).Note);
            fixture.Store.FailWrites = false;
            await gallery.SaveAsync();
            Assert.False(gallery.HasPendingChanges);
            Assert.Equal("待重试备注", Assert.Single(await fixture.Store.LoadAsync()).Note);
        }
        finally
        {
            fixture.Store.FailWrites = false;
            await main.CloseWorkbenchAsync();
        }
    }

    /// <summary>退出取消未确认的备注弹窗，不等待用户操作，已保存的截图仍可恢复。</summary>
    [AvaloniaFact]
    public async Task ShutdownClosesPendingNotePromptWithoutWaitingForUserInput()
    {
        using var fixture = new CaptureWorkbenchTestContext();
        var main = new MainWindow { DataContext = fixture.Application };
        try
        {
            var workbench = await InitializeAsync(main, fixture);
            var capture = workbench.Panels.CaptureCurrentFrameAsync();
            var dialog = await WaitForNoteAsync(workbench);
            dialog.GetVisualDescendants().OfType<CaptureNoteDialogView>().Single()
                .FindControl<TextBox>("NoteInput")!.Text = "未确认草稿";
            await main.CloseWorkbenchAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await capture;
            Assert.False(main.IsVisible);
            Assert.False(dialog.IsVisible);
            Assert.Equal("", Assert.Single(await fixture.Store.LoadAsync()).Note);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    private static async Task<WorkbenchSession> InitializeAsync(MainWindow main, CaptureWorkbenchTestContext fixture)
    {
        main.Show();
        await main.Initialization;
        await fixture.Application.GalleryInitialization;
        fixture.EnablePlayer();
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<WorkbenchSession>(main.Workbench);
    }

    private static async Task<Window> WaitForNoteAsync(WorkbenchSession workbench)
    {
        var completion = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
        workbench.Windows.ModalChanged += Inspect;
        try
        {
            Inspect();
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            workbench.Windows.ModalChanged -= Inspect;
        }

        void Inspect()
        {
            if (workbench.Windows.FindAuxiliary("capture-note") is { IsVisible: true } dialog)
            {
                completion.TrySetResult(dialog);
            }
        }
    }
}
