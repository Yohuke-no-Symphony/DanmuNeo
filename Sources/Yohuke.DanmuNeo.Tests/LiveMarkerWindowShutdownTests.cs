using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证主窗口退出的取消、并发重入与保存失败边界。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class LiveMarkerWindowShutdownTests
{
    /// <summary>退出确认进行中重复关闭不重复询问，取消后窗口、布局和记录文档仍可使用。</summary>
    [AvaloniaFact]
    public async Task RepeatedClosePromptsOnceAndCancelKeepsWindowAndRoomUsable()
    {
        await using var context = new LiveMarkerApplicationTestContext();
        var room = context.FirstRoom;
        await context.OpenDirtyAsync(room, "退出取消前的记录");
        var main = new MainWindow { DataContext = context.Application };
        var decision = new TaskCompletionSource<LiveMarkerLeaveDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? firstClose = null;
        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            var root = workbench.Layouts.RootDock;
            var confirmations = 0;
            context.Application.ConfirmLiveMarkerLeaveAsync = _ =>
            {
                confirmations++;
                requested.TrySetResult();
                return decision.Task;
            };

            firstClose = main.CloseWorkbenchAsync();
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await main.CloseWorkbenchAsync();
            Assert.Equal(1, confirmations);
            Assert.True(main.IsVisible);
            decision.SetResult(LiveMarkerLeaveDecision.Cancel);
            await firstClose;
            Dispatcher.UIThread.RunJobs();

            Assert.True(main.IsVisible);
            Assert.True(main.IsEnabled);
            Assert.Same(workbench, main.Workbench);
            Assert.Same(root, workbench.Layouts.RootDock);
            Assert.False(workbench.Windows.IsShutdownPending);
            Assert.True(room.LiveMarker.CanEdit);
            Assert.True(room.LiveMarker.IsDirty);
            room.LiveMarker.DraftText = "取消后继续记录";
            room.LiveMarker.AddMarkerCommand.Execute(null);
            Assert.Equal(2, room.LiveMarker.Entries.Count);
            Assert.Empty(context.Files.Saves);
        }
        finally
        {
            decision.TrySetResult(LiveMarkerLeaveDecision.Discard);
            if (firstClose is not null)
            {
                await firstClose;
            }
            context.Application.ConfirmLiveMarkerLeaveAsync = _ => Task.FromResult(LiveMarkerLeaveDecision.Discard);
            await main.CloseWorkbenchAsync();
            main.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>记录保存失败不会提前关闭或释放窗口，修复后可再次保存并退出。</summary>
    [AvaloniaFact]
    public async Task FailedMarkerSaveKeepsWorkbenchAliveUntilSuccessfulRetry()
    {
        await using var context = new LiveMarkerApplicationTestContext();
        var room = context.FirstRoom;
        var path = await context.OpenDirtyAsync(room, "退出失败后应保存的记录");
        var main = new MainWindow { DataContext = context.Application };
        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            var root = workbench.Layouts.RootDock;
            context.Application.ConfirmLiveMarkerLeaveAsync = _ => Task.FromResult(LiveMarkerLeaveDecision.Save);
            context.Files.SaveException = new IOException("退出时模拟文件写入失败");

            await main.CloseWorkbenchAsync();

            Assert.True(main.IsVisible);
            Assert.True(main.IsEnabled);
            Assert.Same(root, workbench.Layouts.RootDock);
            Assert.False(workbench.Windows.IsShutdownPending);
            Assert.True(room.LiveMarker.CanEdit);
            Assert.True(room.LiveMarker.IsDirty);
            Assert.Empty(context.Files.Documents[path].Marker);
            Assert.Single(context.Files.Saves);
            context.Files.SaveException = null;

            await main.CloseWorkbenchAsync();

            Assert.False(main.IsVisible);
            Assert.False(room.LiveMarker.CanEdit);
            Assert.Equal("退出失败后应保存的记录", Assert.Single(context.Files.Documents[path].Marker).Message);
            Assert.Equal(2, context.Files.Saves.Count);
        }
        finally
        {
            context.Files.SaveException = null;
            context.Application.ConfirmLiveMarkerLeaveAsync = _ => Task.FromResult(LiveMarkerLeaveDecision.Discard);
            await main.CloseWorkbenchAsync();
            main.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>工作台尚未创建时关闭也必须执行主视图模型的记录保护。</summary>
    [AvaloniaFact]
    public async Task ClosingWithoutWorkbenchStillChecksUnsavedMarkerDocument()
    {
        await using var context = new LiveMarkerApplicationTestContext();
        var room = context.FirstRoom;
        await context.OpenDirtyAsync(room, "尚未创建工作台的记录");
        var main = new MainWindow { DataContext = context.Application };
        try
        {
            var confirmations = 0;
            context.Application.ConfirmLiveMarkerLeaveAsync = _ =>
            {
                confirmations++;
                return Task.FromResult(LiveMarkerLeaveDecision.Cancel);
            };

            await main.CloseWorkbenchAsync();

            Assert.Null(main.Workbench);
            Assert.Equal(1, confirmations);
            Assert.True(room.LiveMarker.CanEdit);
            Assert.True(room.LiveMarker.IsDirty);
            Assert.Empty(context.Files.Saves);
        }
        finally
        {
            context.Application.ConfirmLiveMarkerLeaveAsync = _ => Task.FromResult(LiveMarkerLeaveDecision.Discard);
            await main.CloseWorkbenchAsync();
            main.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
