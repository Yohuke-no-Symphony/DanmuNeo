using System.Threading;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证换牌请求的账号、当前房间及写入顺序边界。</summary>
public class MedalSwitchCoordinatorTests
{
    /// <summary>账号重新验证期间放弃的活跃目标，会在同一凭据恢复有效后重新排队。</summary>
    [Fact]
    public async Task ValidationRecoveryRetriesAnInterruptedAutomaticTarget()
    {
        var validation = new ControlledAccountValidationService();
        using var sessions = new BilibiliAccountSessionService(validation);
        using var log = new AppLogService();
        var account = Account(true);
        sessions.Register(account);
        var initialValidation = sessions.ValidateAsync(account);
        validation.Requests[0].SetResult(new() { Status = AccountValidationStatus.Valid, Uid = 42 });
        await initialValidation;
        var service = new FakeMedalService
        {
            OwnerReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseOwnerRead = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);
        var automatic = coordinator.SetActiveRoomAsync(account, "100");
        await service.OwnerReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var repeatedValidation = sessions.ValidateAsync(account);
        service.ReleaseOwnerRead.SetResult();
        await automatic;
        Assert.Empty(service.Writes);

        validation.Requests[1].SetResult(new() { Status = AccountValidationStatus.Valid, Uid = 42 });
        await repeatedValidation;
        await coordinator.WhenIdleAsync();

        Assert.Equal(2, service.OwnerReadCount);
        Assert.Equal("wear:11", Assert.Single(service.Writes));
    }

    /// <summary>自动关闭不查询房间或牌墙，启用后解析真实 UID 且相同目标不重复查询。</summary>
    [Fact]
    public async Task DisabledAutomaticDoesNotRequestAndStableTargetIsDeduplicated()
    {
        var service = new FakeMedalService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask, () => TimeSpan.FromSeconds(3), log);
        await coordinator.SetActiveRoomAsync(account, "999");
        Assert.Equal(0, service.OwnerReadCount);
        account.AutoSwitchMedal = true;
        await coordinator.WhenIdleAsync();
        Assert.Equal(1, service.OwnerReadCount);
        Assert.Equal(["wear:11"], service.Writes);
        var reads = service.ReadCount;
        await coordinator.SetActiveRoomAsync(account, "999");
        Assert.Equal(reads, service.ReadCount);
    }

    /// <summary>列表不完整或查询错误时不会误摘正在佩戴的牌子。</summary>
    [Fact]
    public async Task IncompleteOrFailedWallNeverTakesOff()
    {
        var service = new FakeMedalService { Collection = new([new(22, 200, "其他", 10, true)], false) };
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = Account(true);
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask, () => TimeSpan.FromSeconds(3), log);
        await coordinator.SetActiveTargetAsync(account, 100);
        Assert.Empty(service.Writes);
        service.ReadError = new IOException("unavailable");
        await coordinator.SetActiveTargetAsync(account, 300);
        Assert.Empty(service.Writes);
        Assert.NotEmpty(log.Entries);
    }

    /// <summary>完整列表确认未持有目标牌时才使用当前 medal ID 摘牌。</summary>
    [Fact]
    public async Task CompleteWallCanConfirmNoMatchingMedal()
    {
        var service = new FakeMedalService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = Account(true);
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask, () => TimeSpan.FromSeconds(3), log);
        await coordinator.SetActiveTargetAsync(account, 999);
        Assert.Equal(["off:22"], service.Writes);
    }

    /// <summary>手动请求先保存关闭自动，再等待在途自动写入结束，最后手动结果生效。</summary>
    [Fact]
    public async Task ManualWaitsForAutomaticWriteAndCannotBeOverwrittenByQueuedTargets()
    {
        var service = new FakeMedalService
        {
            FirstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseFirstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = Account(true);
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        var saved = false;
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => { saved = !account.AutoSwitchMedal; return Task.CompletedTask; }, () => TimeSpan.FromSeconds(3), log);
        var automatic = coordinator.SetActiveTargetAsync(account, 100);
        await service.FirstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _ = coordinator.SetActiveTargetAsync(account, 300);
        var manual = coordinator.WearManuallyAsync(account, 22);
        Assert.True(saved);
        Assert.False(account.AutoSwitchMedal);
        Assert.Equal(["wear:11"], service.Writes);
        service.ReleaseFirstWrite.SetResult();
        await Task.WhenAll(automatic, manual);
        await coordinator.WhenIdleAsync();
        Assert.Equal(["wear:11", "wear:22"], service.Writes);
        Assert.True(service.Collection.Items.Single(item => item.Id == 22).IsWearing);
    }

    /// <summary>快速切房仅保留最新尚未开始的目标。</summary>
    [Fact]
    public async Task LatestPendingAutomaticTargetWins()
    {
        var service = new FakeMedalService { FirstWriteStarted = new(), ReleaseFirstWrite = new() };
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = Account(true);
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask, () => TimeSpan.FromSeconds(3), log);
        var first = coordinator.SetActiveTargetAsync(account, 100);
        await service.FirstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _ = coordinator.SetActiveTargetAsync(account, 200);
        var last = coordinator.SetActiveTargetAsync(account, 300);
        service.ReleaseFirstWrite.SetResult();
        await Task.WhenAll(first, last);
        Assert.Equal(["wear:11", "wear:33"], service.Writes);
    }

    /// <summary>关闭自动设置保存失败时不发送手动写请求。</summary>
    [Fact]
    public async Task FailedPreferenceSavePreventsManualMutation()
    {
        var service = new FakeMedalService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = Account(true);
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.FromException(new IOException("save failed")), () => TimeSpan.FromSeconds(3), log);
        await Assert.ThrowsAsync<IOException>(() => coordinator.WearManuallyAsync(account, 11));
        Assert.False(account.AutoSwitchMedal);
        Assert.Empty(service.Writes);
    }

    /// <summary>启用偏好成功保存前不发请求，失败后恢复关闭状态。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticEnableWaitsForSuccessfulPreferenceSave(bool failSave)
    {
        var service = new FakeMedalService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => save.Task, () => TimeSpan.FromSeconds(3), log);
        await coordinator.SetActiveRoomAsync(account, "999");
        var enabling = coordinator.SetAutomaticAsync(account, true);
        Assert.Equal(0, service.OwnerReadCount);
        Assert.Equal(0, service.ReadCount);
        Assert.Empty(service.Writes);
        if (failSave)
        {
            save.SetException(new IOException("save failed"));
            await Assert.ThrowsAsync<IOException>(() => enabling);
            await coordinator.WhenIdleAsync();
            Assert.False(account.AutoSwitchMedal);
            Assert.Equal(0, service.OwnerReadCount);
            Assert.Equal(0, service.ReadCount);
            Assert.Empty(service.Writes);
        }
        else
        {
            save.SetResult();
            await enabling;
            Assert.Equal(1, service.OwnerReadCount);
            Assert.Equal(["wear:11"], service.Writes);
        }
    }

    /// <summary>手动意图等待保存时发生凭据编辑或删除账号会撤销旧操作，不借用新凭据。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualIntentIsCanceledWhenCredentialsChangeDuringSave(bool removeAccount)
    {
        var service = new FakeMedalService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = Account(true);
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => save.Task, () => TimeSpan.FromSeconds(3), log);
        var manual = coordinator.WearManuallyAsync(account, 11);
        if (removeAccount)
        {
            sessions.Remove(account.Id);
        }
        else
        {
            account.Cookie += ";newCredential=value";
            await sessions.ValidateAsync(account);
        }
        save.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manual);
        Assert.Equal(0, service.ReadCount);
        Assert.Empty(service.Writes);
    }

    /// <summary>迟到的旧凭据牌墙响应不会触发自动佩戴。</summary>
    [Fact]
    public async Task DelayedWallForOldCredentialsCannotWrite()
    {
        var service = new FakeMedalService
        {
            ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = Account(true);
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask, () => TimeSpan.FromSeconds(3), log);
        var automatic = coordinator.SetActiveTargetAsync(account, 100);
        await service.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        account.Cookie += ";newCredential=value";
        service.ReleaseRead.SetResult();
        await automatic;
        Assert.Empty(service.Writes);
    }

    /// <summary>明确未登录错误撤销当前账号会话。</summary>
    [Fact]
    public async Task AuthenticationFailureInvalidatesAccount()
    {
        var service = new FakeMedalService { ReadError = new BilibiliAuthenticationException("expired") };
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask, () => TimeSpan.FromSeconds(3), log);
        await Assert.ThrowsAsync<BilibiliAuthenticationException>(() => coordinator.RefreshAsync(account));
        Assert.False(sessions.CanSend(account.Id, account.Cookie));
    }

    internal static BilibiliAccount Account(bool automatic = false) => new()
    {
        Cookie = "buvid3=fake;SESSDATA=fake;bili_jct=fake;DedeUserID=42", AutoSwitchMedal = automatic
    };
}
