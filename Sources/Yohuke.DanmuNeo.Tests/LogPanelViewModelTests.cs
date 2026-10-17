using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证日志筛选、完整复制和 UI 线程通知合并。
/// </summary>
public class LogPanelViewModelTests
{
    /// <summary>级别与搜索同时作用，搜索可匹配未展开的异常详情并保留有效选择。</summary>
    [AvaloniaFact]
    public void FiltersFullDetailsAndPreservesOnlyVisibleSelection()
    {
        using var log = new AppLogService();
        log.Information("保存", "已保存");
        log.Warning("监听 / 房间 100", "正在重试");
        log.Error("发送 / 房间 100", "发送失败", new IOException("unique-detail-marker"));
        using var viewModel = new LogPanelViewModel(log);
        var error = viewModel.Entries[2];
        viewModel.SelectedEntry = error;

        viewModel.FilterText = "UNIQUE-detail";

        Assert.Same(error, Assert.Single(viewModel.Entries));
        Assert.Same(error, viewModel.SelectedEntry);
        Assert.True(viewModel.HasSelection);
        Assert.Contains("unique-detail-marker", viewModel.CopyVisibleText());
        Assert.DoesNotContain("已保存", viewModel.CopyVisibleText());

        viewModel.FilterIndex = 1;
        Assert.Empty(viewModel.Entries);
        Assert.Null(viewModel.SelectedEntry);
        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasSelection);
        viewModel.FilterText = string.Empty;
        Assert.Equal("已保存", Assert.Single(viewModel.Entries).Message);
        viewModel.FilterIndex = 2;
        Assert.Equal("正在重试", Assert.Single(viewModel.Entries).Message);
        viewModel.FilterIndex = 3;
        Assert.Same(error, Assert.Single(viewModel.Entries));
        viewModel.FilterIndex = 99;
        Assert.Equal(3, viewModel.FilterIndex);
    }

    /// <summary>后台批量通知在 UI 队列只占用一次刷新，所有条目仍能显示。</summary>
    [AvaloniaFact]
    public void BackgroundBurstCoalescesIntoOneUiRefresh()
    {
        using var log = new AppLogService();
        using var viewModel = new LogPanelViewModel(log);
        var updates = 0;
        var refreshedOnUiThread = true;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LogPanelViewModel.Entries))
            {
                updates++;
                refreshedOnUiThread &= Dispatcher.UIThread.CheckAccess();
            }
        };

        Task.Run(() => Parallel.For(0, 400, value => log.Information("后台", value.ToString())))
            .GetAwaiter().GetResult();

        Assert.Equal(0, updates);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, updates);
        Assert.True(refreshedOnUiThread);
        Assert.Equal(400, viewModel.Entries.Count);
    }

    /// <summary>仅有明确的阅读状态才清未读，清空命令同时更新列表和选择。</summary>
    [AvaloniaFact]
    public void ReadingAndClearUpdateUnreadWithoutOwningService()
    {
        using var log = new AppLogService();
        using var viewModel = new LogPanelViewModel(log);
        var reading = false;
        viewModel.SetReadingState(() => reading);
        log.Error("监听", "失败");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, viewModel.UnreadErrorCount);
        reading = true;
        viewModel.MarkVisibleEntriesRead();
        Assert.Equal(0, log.UnreadErrorCount);
        reading = false;
        log.Error("监听", "另一次失败");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, viewModel.UnreadErrorCount);
        viewModel.SelectedEntry = viewModel.Entries[0];
        viewModel.ClearCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(viewModel.Entries);
        Assert.Null(viewModel.SelectedEntry);
        Assert.Equal(0, viewModel.UnreadErrorCount);
        viewModel.Dispose();
        log.Information("保存", "仍可使用共享服务");
        Assert.Single(log.Entries);
    }

    /// <summary>释放会取消订阅，已经排队的刷新也不会再次改写面板。</summary>
    [AvaloniaFact]
    public void DisposeIgnoresQueuedAndFutureChanges()
    {
        using var log = new AppLogService();
        var viewModel = new LogPanelViewModel(log);
        var updates = 0;
        viewModel.PropertyChanged += (_, _) => updates++;
        log.Error("监听", "待刷新");
        viewModel.Dispose();
        log.Information("保存", "释放后");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, updates);
        Assert.Empty(viewModel.Entries);
    }
}
