using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>使用真实控件与离线服务验证禁言管理入口。</summary>
public class ModerationPanelTests
{
    /// <summary>没有当前房间时仍可以扫描，并且账号选择绑定到独立管理器。</summary>
    [AvaloniaFact]
    public async Task PanelUsesIndependentContextAndExplicitAccountSelection()
    {
        using var main = new LiveRoomFeedViewTestContext();
        using var fixture = new ModerationManagerTestContext();
        using var context = new WorkbenchContext(main.ViewModel);
        main.ViewModel.SelectedWorkspace!.SelectedRoom = null;
        using var panel = new ModerationPanelView(context, fixture.Manager);
        var window = new Window { Width = 380, Height = 640, Content = panel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await fixture.Manager.RefreshTask;
            Assert.True(panel.IsEnabled);
            Assert.Same(fixture.Manager, panel.DataContext);
            Assert.Equal(2, fixture.Manager.Records.Count);
            var accounts = panel.FindControl<ComboBox>("ModerationAccount")!;
            Assert.NotNull(accounts);
            accounts.SelectedItem = fixture.Accounts[1];
            Dispatcher.UIThread.RunJobs();
            await fixture.Manager.RefreshTask;
            Assert.Same(fixture.Accounts[1], fixture.Manager.SelectedAccount);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>实际搜索和行内写操作按钮使用目标用户或名单记录作为参数。</summary>
    [AvaloniaFact]
    public async Task SearchAndRecordActionsUseBoundCommandsAndParameters()
    {
        using var main = new LiveRoomFeedViewTestContext();
        using var fixture = new ModerationManagerTestContext();
        using var context = new WorkbenchContext(main.ViewModel);
        fixture.Service.SearchResponseAsync = (_, _, _) => Task.FromResult(new ModerationUserSearchPage
        {
            Users = [new() { Uid = 31, UserName = "被搜索用户" }]
        });
        using var panel = new ModerationPanelView(context, fixture.Manager);
        var window = new Window { Width = 380, Height = 760, Content = panel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await fixture.Manager.RefreshTask;
            panel.FindControl<TextBox>("ModerationSearch")!.Text = "31";
            Dispatcher.UIThread.RunJobs();
            var search = panel.FindControl<Button>("ModerationFindUser")!;
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(search.Command).ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var mute = panel.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ModerationMuteUser");
            Assert.Same(Assert.Single(fixture.Manager.UserSearchResults), mute.CommandParameter);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(mute.Command).ExecuteAsync(mute.CommandParameter);
            Assert.Equal(31, Assert.Single(fixture.Service.MuteRequests).UserUid);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var unmute = panel.GetVisualDescendants().OfType<Button>().First(button => button.Name == "ModerationUnmuteUser");
            var record = Assert.IsType<ModerationRecord>(unmute.CommandParameter);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(unmute.Command).ExecuteAsync(record);
            Assert.Equal(record.RecordId, Assert.Single(fixture.Service.UnmuteRequests).RecordId);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>卸载及隐藏保留选择和已读名单，面板释放不释放共享管理器。</summary>
    [AvaloniaFact]
    public async Task DetachPreservesLoadedStateAndSharedManager()
    {
        using var main = new LiveRoomFeedViewTestContext();
        using var fixture = new ModerationManagerTestContext();
        using var context = new WorkbenchContext(main.ViewModel);
        using var panel = new ModerationPanelView(context, fixture.Manager);
        var window = new Window { Width = 380, Height = 640, Content = panel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await fixture.Manager.RefreshTask;
            var account = fixture.Manager.SelectedAccount;
            window.Content = null;
            fixture.Service.RaiseChanged(100);
            Assert.Equal(2, fixture.Service.ListRequests.Count);
            Assert.Equal(2, fixture.Manager.Records.Count);
            window.Content = panel;
            Dispatcher.UIThread.RunJobs();
            await fixture.Manager.RefreshTask;
            Assert.Same(account, fixture.Manager.SelectedAccount);
            Assert.Equal(3, fixture.Service.ListRequests.Count);
            panel.Dispose();
            await fixture.Manager.ActivateAsync();
            Assert.Equal(2, fixture.Manager.Records.Count);
        }
        finally
        {
            window.Close();
        }
    }
    /// <summary>祖先容器隐藏同样暂停读取，快速显示会重试中断房间，不等待旧响应。</summary>
    [AvaloniaFact]
    public async Task AncestorVisibilityCancelsReadingAndResumesDirtyRoom()
    {
        using var main = new LiveRoomFeedViewTestContext();
        using var fixture = new ModerationManagerTestContext();
        using var context = new WorkbenchContext(main.ViewModel);
        using var panel = new ModerationPanelView(context, fixture.Manager);
        var container = new Border { Child = panel };
        var window = new Window { Width = 380, Height = 640, Content = container };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            await fixture.Manager.RefreshTask;
            var pending = new TaskCompletionSource<IReadOnlyList<ModerationRecord>>();
            fixture.Service.ListResponseAsync = (_, _, _) => pending.Task;
            fixture.Service.RaiseChanged(100);
            var oldRead = fixture.Manager.RefreshTask;
            var oldToken = fixture.Service.ListRequests[^1].Token;
            container.IsVisible = false;
            Assert.True(oldToken.IsCancellationRequested);
            Assert.Equal(2, fixture.Manager.Records.Count);
            fixture.Service.ListResponseAsync = (roomId, _, _) => Task.FromResult<IReadOnlyList<ModerationRecord>>
                ([new() { RoomId = roomId, RecordId = 99, UserUid = 31 }]);
            container.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            await fixture.Manager.RefreshTask;
            Assert.Equal(4, fixture.Service.ListRequests.Count);
            pending.SetResult([]);
            await oldRead;
            Assert.Equal(99, fixture.Manager.Records.Single(record => record.RoomId == 100).RecordId);
        }
        finally
        {
            window.Close();
        }
    }
}
