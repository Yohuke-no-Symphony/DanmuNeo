using System.Collections.ObjectModel;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证粉丝牌面板在无房间、固定账号和凭据变化时的行为。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class AccountMedalsViewModelTests
{
    /// <summary>异步保存失败后的偏好回滚与绑定通知仍在发起操作的界面上下文。</summary>
    [AvaloniaFact]
    public async Task AsynchronousPreferenceRollbackKeepsUiContext()
    {
        using var log = new AppLogService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new MedalSwitchCoordinator(new FakeMedalService(), sessions,
            () => save.Task, () => TimeSpan.FromSeconds(3), log);
        using var viewModel = new AccountMedalsViewModel(new([account]), coordinator, log);
        viewModel.UpdateCurrentAccount(account);
        await viewModel.ActivateAsync();
        var accountNotificationAccess = new List<bool>();
        var bindingNotificationAccess = new List<bool>();
        account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BilibiliAccount.AutoSwitchMedal))
            {
                accountNotificationAccess.Add(Dispatcher.UIThread.CheckAccess());
            }
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountMedalsViewModel.AutoSwitchMedal))
            {
                bindingNotificationAccess.Add(Dispatcher.UIThread.CheckAccess());
            }
        };

        viewModel.AutoSwitchMedal = true;
        save.SetException(new IOException("模拟异步保存失败"));
        await viewModel.PendingOperation;
        Dispatcher.UIThread.RunJobs();

        Assert.False(account.AutoSwitchMedal);
        Assert.Equal(2, accountNotificationAccess.Count);
        Assert.All(accountNotificationAccess, Assert.True);
        Assert.NotEmpty(bindingNotificationAccess);
        Assert.All(bindingNotificationAccess, Assert.True);
    }

    /// <summary>隐藏时更新与固定账号不查询牌墙，激活才读取且卸载后保留缓存。</summary>
    [AvaloniaFact]
    public async Task HiddenAccountChangesStayOfflineUntilActivation()
    {
        using var log = new AppLogService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        var service = new FakeMedalService();
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask, () => TimeSpan.FromSeconds(3), log);
        using var viewModel = new AccountMedalsViewModel(new([account]), coordinator, log);
        viewModel.UpdateCurrentAccount(account);
        viewModel.FollowCurrentAccount = false;
        Assert.Equal(0, service.ReadCount);
        await viewModel.ActivateAsync();
        Assert.Equal(1, service.ReadCount);
        viewModel.Deactivate();
        Assert.Equal(3, viewModel.Medals.Count);
        viewModel.UpdateCurrentAccount(null);
        Assert.Same(account, viewModel.SelectedAccount);
        Assert.Equal(1, service.ReadCount);
    }

    /// <summary>隐藏撤销迟到只读响应，重新激活会刷新且不会被旧任务结束状态覆盖。</summary>
    [AvaloniaFact]
    public async Task DeactivationCancelsOnlyReadAndReactivationRefreshes()
    {
        using var log = new AppLogService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        var service = new FakeMedalService
        {
            ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask, () => TimeSpan.FromSeconds(3), log);
        using var viewModel = new AccountMedalsViewModel(new([account]), coordinator, log);
        viewModel.UpdateCurrentAccount(account);
        var first = viewModel.ActivateAsync();
        await service.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.Deactivate();
        Assert.Empty(viewModel.Medals);
        var reactivated = viewModel.ActivateAsync();
        service.ReleaseRead.SetResult();
        await Task.WhenAll(first, reactivated);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, service.ReadCount);
        Assert.Equal(3, viewModel.Medals.Count);
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>默认跟随当前账号，固定选择后不受房间账号变化影响。</summary>
    [AvaloniaFact]
    public async Task FollowsCurrentAccountUntilExplicitlyPinned()
    {
        using var log = new AppLogService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        var first = MedalSwitchCoordinatorTests.Account();
        var second = MedalSwitchCoordinatorTests.Account();
        sessions.Register(first);
        sessions.Register(second);
        await sessions.ValidateAsync(first);
        await sessions.ValidateAsync(second);
        await using var coordinator = new MedalSwitchCoordinator(new FakeMedalService(), sessions, () => Task.CompletedTask, () => TimeSpan.FromSeconds(3), log);
        using var viewModel = new AccountMedalsViewModel(new([first, second]), coordinator, log);
        Assert.True(viewModel.FollowCurrentAccount);
        Assert.Null(viewModel.SelectedAccount);
        viewModel.UpdateCurrentAccount(first);
        await viewModel.ActivateAsync();
        await viewModel.PendingOperation;
        viewModel.FollowCurrentAccount = false;
        viewModel.UpdateCurrentAccount(second);
        Assert.Same(first, viewModel.SelectedAccount);
        viewModel.SelectedAccount = second;
        await viewModel.PendingOperation;
        viewModel.UpdateCurrentAccount(null);
        Assert.Same(second, viewModel.SelectedAccount);
        Assert.Equal(3, viewModel.Medals.Count);
    }

    /// <summary>手动选择牌子会保存关闭自动且写后更新账号级当前状态。</summary>
    [AvaloniaFact]
    public async Task ManualWearDisablesAutomaticAndRefreshesWearingState()
    {
        using var log = new AppLogService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        var account = MedalSwitchCoordinatorTests.Account(true);
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        var service = new FakeMedalService();
        var saves = 0;
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => { saves++; return Task.CompletedTask; }, () => TimeSpan.FromSeconds(3), log);
        using var viewModel = new AccountMedalsViewModel(new([account]), coordinator, log);
        viewModel.FollowCurrentAccount = false;
        viewModel.SelectedAccount = account;
        await viewModel.ActivateAsync();
        await viewModel.PendingOperation;
        viewModel.SelectedMedal = viewModel.Medals.First(item => item.Id == 11);
        await viewModel.WearSelectedAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.False(account.AutoSwitchMedal);
        Assert.Equal(1, saves);
        Assert.True(viewModel.Medals.Single(item => item.Id == 11).IsWearing);
    }

    /// <summary>一次点击佩戴后保留行选择和未变化的行引用，摘下也只提交一次。</summary>
    [AvaloniaFact]
    public async Task SingleWearAndTakeOffKeepSelectionWithDelayedWall()
    {
        using var log = new AppLogService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        var service = new FakeMedalService();
        var old = service.Collection;
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);
        using var viewModel = new AccountMedalsViewModel(new([account]), coordinator, log);
        viewModel.UpdateCurrentAccount(account);
        await viewModel.ActivateAsync();
        Dispatcher.UIThread.RunJobs();
        viewModel.SelectedMedal = viewModel.Medals.Single(item => item.Id == 11);
        var unchanged = viewModel.Medals.Single(item => item.Id == 33);
        service.ReadResponse = _ => old;

        await viewModel.WearSelectedCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(11, viewModel.SelectedMedal?.Id);
        Assert.True(viewModel.SelectedMedal?.IsWearing);
        Assert.Same(unchanged, viewModel.Medals.Single(item => item.Id == 33));
        Assert.False(viewModel.WearSelectedCommand.CanExecute(null));
        Assert.True(viewModel.TakeOffCommand.CanExecute(null));
        Assert.Equal(["wear:11"], service.Writes);
        await viewModel.TakeOffCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["wear:11", "off:11"], service.Writes);
        Assert.DoesNotContain(viewModel.Medals, item => item.IsWearing);
        Assert.Equal(11, viewModel.SelectedMedal?.Id);
        Assert.False(viewModel.TakeOffCommand.CanExecute(null));
    }

    /// <summary>Cookie变化立即清空旧牌墙，账号删除后不自动切换到其他账号。</summary>
    [AvaloniaFact]
    public async Task CredentialAndAccountRemovalInvalidatePanelState()
    {
        using var log = new AppLogService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(new FakeMedalService(), sessions, () => Task.CompletedTask, () => TimeSpan.FromSeconds(3), log);
        var accounts = new ObservableCollection<BilibiliAccount> { account };
        using var viewModel = new AccountMedalsViewModel(accounts, coordinator, log);
        viewModel.FollowCurrentAccount = false;
        viewModel.SelectedAccount = account;
        await viewModel.ActivateAsync();
        await viewModel.PendingOperation;
        account.Cookie = "SESSDATA=new;bili_jct=new";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(viewModel.Medals);
        accounts.Remove(account);
        Assert.Null(viewModel.SelectedAccount);
    }
}
