using System.Threading;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证退出保存失败时保留工作台，并允许修复存储后重新退出。
/// </summary>
public class WorkspaceLayoutShutdownTests
{
    /// <summary>
    /// 写入失败不能拆除浮窗或封死布局操作，恢复存储后重试保存最新状态。
    /// </summary>
    [AvaloniaFact]
    public async Task FailedShutdownSaveKeepsWorkbenchUsableAndCanRetry()
    {
        var failWrites = false;
        await using var context = new WorkbenchLayoutTestContext(replaceFile: (source, destination) =>
        {
            if (failWrites)
            {
                throw new IOException("模拟布局替换失败。");
            }

            File.Move(source, destination, true);
        });
        context.Show();
        context.Controller.FloatPanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Pump();
        await context.Controller.FlushAsync();
        var floating = Assert.Single(context.Controller.FloatingWindows);
        var root = context.Controller.RootDock;
        var closed = new List<string>();
        context.Controller.PanelClosedByUser += closed.Add;

        try
        {
            failWrites = true;
            context.Controller.HidePanel(WorkbenchPanelIds.HISTORY, false);
            context.Pump();
            await Assert.ThrowsAsync<IOException>(() => context.Controller.DisposeAsync().AsTask());

            Assert.Same(root, context.Controller.RootDock);
            Assert.Same(root, context.Controller.Host.Layout);
            Assert.Same(floating, Assert.Single(context.Controller.FloatingWindows));
            Assert.True(floating.IsVisible);
            Assert.Empty(context.UnregisteredWindows);
            Assert.Empty(closed);
            Assert.Contains(WorkbenchPanelIds.HISTORY, context.Controller.Capture().HiddenPanelIds);
            context.Controller.ShowPanel(WorkbenchPanelIds.HISTORY);
            context.Pump();
            Assert.True(context.Controller.IsVisible(WorkbenchPanelIds.HISTORY));

            failWrites = false;
            await context.Controller.DisposeAsync();
            var restored = await context.Store.LoadAsync();
            Assert.Single(restored.CurrentLayout.FloatingWindows);
            Assert.DoesNotContain(WorkbenchPanelIds.HISTORY, restored.CurrentLayout.HiddenPanelIds);
            Assert.Empty(context.Controller.FloatingWindows);
            Assert.Single(context.UnregisteredWindows);
            Assert.Empty(closed);
        }
        finally
        {
            failWrites = false;
        }
    }

    /// <summary>
    /// 退出前已经成功刷新相同快照时，销毁阶段不产生第二次写入。
    /// </summary>
    [AvaloniaFact]
    public async Task SuccessfulShutdownFlushIsNotWrittenAgainDuringDisposal()
    {
        var rejectFurtherWrites = false;
        var writeCount = 0;
        await using var context = new WorkbenchLayoutTestContext(replaceFile: (source, destination) =>
        {
            if (rejectFurtherWrites)
            {
                throw new IOException("销毁阶段不应再次写入。");
            }

            writeCount++;
            File.Move(source, destination, true);
        });
        context.Show();
        context.Controller.FloatPanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Pump();
        await context.Controller.FlushAsync();
        var savedWriteCount = writeCount;

        try
        {
            rejectFurtherWrites = true;
            await context.Controller.DisposeAsync();
            await context.Controller.DisposeAsync();

            Assert.Equal(savedWriteCount, writeCount);
            Assert.Empty(context.Controller.FloatingWindows);
            Assert.Single(context.UnregisteredWindows);
            var restored = await context.Store.LoadAsync();
            Assert.Single(restored.CurrentLayout.FloatingWindows);
        }
        finally
        {
            rejectFurtherWrites = false;
        }
    }

    /// <summary>
    /// 调度器退出写入失败后恢复接收快照，下一次退出只保存新的最终状态。
    /// </summary>
    [Fact]
    public async Task FailedSchedulerDisposalCanAcceptNewSnapshotAndRetry()
    {
        var failWrites = true;
        var saved = new List<string>();
        await using var scheduler = new WorkspaceLayoutSaveScheduler((file, _) =>
        {
            if (failWrites)
            {
                throw new IOException("模拟退出刷新失败。");
            }

            saved.Add(file.CurrentPresetId);
            return Task.CompletedTask;
        }, (_, cancellation) => Task.Delay(Timeout.InfiniteTimeSpan, cancellation));

        try
        {
            scheduler.Schedule(WorkspaceLayoutPresets.CreateDefaultFile());
            await Assert.ThrowsAsync<IOException>(() => scheduler.DisposeAsync().AsTask());
            Assert.IsType<IOException>(scheduler.LastError);
            failWrites = false;
            scheduler.Schedule(new()
            {
                CurrentPresetId = WorkspaceLayoutPresets.TRANSLATION,
                CurrentLayout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.TRANSLATION)
            });
            await scheduler.DisposeAsync();
            await scheduler.DisposeAsync();

            Assert.Equal([WorkspaceLayoutPresets.TRANSLATION], saved);
            Assert.Null(scheduler.LastError);
            Assert.Throws<ObjectDisposedException>(() => scheduler.Schedule(WorkspaceLayoutPresets.CreateDefaultFile()));
        }
        finally
        {
            failWrites = false;
        }
    }
}
