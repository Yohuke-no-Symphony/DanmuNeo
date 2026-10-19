using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>模拟写入已接受但牌墙读取滞后，保证单次操作不重复提交。</summary>
public class MedalWriteConfirmationTests
{
    /// <summary>手动佩戴与摘下在短暂旧读后确认目标状态，始终只写一次。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedWallConvergesWithoutRepeatingWrite(bool takeOff)
    {
        var service = new FakeMedalService();
        var old = service.Collection;
        service.ReadResponse = count => count < 4 ? old : service.Collection;
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);

        var result = takeOff
            ? await coordinator.TakeOffManuallyAsync(account)
            : await coordinator.WearManuallyAsync(account, 11);

        Assert.Equal([takeOff ? "off:22" : "wear:11"], service.Writes);
        Assert.Equal(4, service.ReadCount);
        Assert.Equal(takeOff ? null : 11L, result.Items.SingleOrDefault(item => item.IsWearing)?.Id);
        Assert.False(result.IsWearingStatePending);
    }

    /// <summary>写后读取失败不会把已成功提交的操作报告为失败，后续刷新继续确认。</summary>
    [Fact]
    public async Task AcceptedWriteSurvivesFailedConfirmationAndStaleRefresh()
    {
        var service = new FakeMedalService();
        var old = service.Collection;
        service.ReadResponse = count => count == 1 ? old : throw new IOException("confirmation unavailable");
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);

        var accepted = await coordinator.WearManuallyAsync(account, 11);
        Assert.True(accepted.IsWearingStatePending);
        Assert.Equal(11, Assert.Single(accepted.Items, item => item.IsWearing).Id);
        service.ReadResponse = _ => old;
        var staleRefresh = await coordinator.RefreshAsync(account);
        Assert.True(staleRefresh.IsWearingStatePending);
        Assert.Equal(11, Assert.Single(staleRefresh.Items, item => item.IsWearing).Id);
        service.ReadResponse = null;
        var confirmed = await coordinator.RefreshAsync(account);
        Assert.False(confirmed.IsWearingStatePending);
        Assert.Equal(11, Assert.Single(confirmed.Items, item => item.IsWearing).Id);
        Assert.Equal(["wear:11"], service.Writes);
    }

    /// <summary>自动换牌也等待读取收敛，不向面板发布旧佩戴状态。</summary>
    [Fact]
    public async Task AutomaticWriteConfirmsAfterDelayedReads()
    {
        var service = new FakeMedalService();
        var old = service.Collection;
        service.ReadResponse = count => count < 4 ? old : service.Collection;
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = MedalSwitchCoordinatorTests.Account(true);
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);
        AccountMedalCollection? published = null;
        coordinator.StateChanged += (_, snapshot) => published = snapshot.Collection;

        await coordinator.SetActiveTargetAsync(account, 100);

        Assert.Equal(["wear:11"], service.Writes);
        Assert.Equal(11, Assert.Single(Assert.IsType<AccountMedalCollection>(published).Items, item => item.IsWearing).Id);
        Assert.Equal(4, service.ReadCount);
    }

    /// <summary>佩戴尚在同步时点击摘下，仍针对刚刚提交的牌子且只提交一次。</summary>
    [Fact]
    public async Task TakeOffAfterPendingWearKeepsLatestIntent()
    {
        var service = new FakeMedalService();
        var old = service.Collection;
        service.ReadResponse = _ => old;
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);

        var wear = await coordinator.WearManuallyAsync(account, 11);
        Assert.True(wear.IsWearingStatePending);
        var takeOff = await coordinator.TakeOffManuallyAsync(account, 11);

        Assert.Equal(["wear:11", "off:11"], service.Writes);
        Assert.DoesNotContain(takeOff.Items, item => item.IsWearing);
        Assert.True(takeOff.IsWearingStatePending);
        service.ReadResponse = null;
        Assert.False((await coordinator.RefreshAsync(account)).IsWearingStatePending);
    }

    /// <summary>写前读取无人佩戴时，仍执行界面中明确指定的摘牌意图。</summary>
    [Fact]
    public async Task ExplicitTakeOffIsNotSkippedByStaleEmptyWearingState()
    {
        var service = new FakeMedalService();
        service.Collection = service.Collection with
        {
            Items = service.Collection.Items.Select(item => item with { IsWearing = false }).ToArray()
        };
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);

        await coordinator.TakeOffManuallyAsync(account, 22);

        Assert.Equal(["off:22"], service.Writes);
    }

    /// <summary>旧凭据下待确认的佩戴状态不能带入重新登录后的牌墙。</summary>
    [Fact]
    public async Task PendingStateDoesNotCrossCredentialBoundary()
    {
        var service = new FakeMedalService();
        var old = service.Collection;
        service.ReadResponse = _ => old;
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);

        Assert.True((await coordinator.WearManuallyAsync(account, 11)).IsWearingStatePending);
        account.Cookie += ";credentialRevision=new";
        await sessions.ValidateAsync(account);
        var refreshed = await coordinator.RefreshAsync(account);

        Assert.False(refreshed.IsWearingStatePending);
        Assert.Equal(22, Assert.Single(refreshed.Items, item => item.IsWearing).Id);
        Assert.Equal(["wear:11"], service.Writes);
    }

    /// <summary>确认目标已佩戴不依赖拥有列表是否完整。</summary>
    [Fact]
    public async Task WearingCanBeConfirmedOnIncompleteWall()
    {
        var service = new FakeMedalService();
        service.Collection = service.Collection with { IsComplete = false };
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);

        var result = await coordinator.WearManuallyAsync(account, 11);

        Assert.False(result.IsWearingStatePending);
        Assert.False(result.IsComplete);
        Assert.Equal(11, Assert.Single(result.Items, item => item.IsWearing).Id);
    }

    /// <summary>等待同步有界，超时后的显式刷新展示服务器现状且不会重复写入。</summary>
    [Fact]
    public async Task PendingOverrideExpiresAndRestoresAuthoritativeWall()
    {
        var service = new FakeMedalService();
        var old = service.Collection;
        service.ReadResponse = _ => old;
        var clock = new ManualSendTimeProvider();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log, clock);

        Assert.True((await coordinator.WearManuallyAsync(account, 11)).IsWearingStatePending);
        clock.Advance(TimeSpan.FromMinutes(1));
        var refreshed = await coordinator.RefreshAsync(account);

        Assert.Equal(MedalWearingSyncState.TimedOut, refreshed.WearingSyncState);
        Assert.Equal(22, Assert.Single(refreshed.Items, item => item.IsWearing).Id);
        Assert.False((await coordinator.RefreshAsync(account)).IsWearingStatePending);
        Assert.Equal(["wear:11"], service.Writes);
    }

    /// <summary>牌墙缺项不能取消界面已捕获的明确摘牌请求。</summary>
    [Fact]
    public async Task ExplicitTakeOffWorksWhenIncompleteWallOmitsMedal()
    {
        var service = new FakeMedalService { Collection = new([], false) };
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);

        await coordinator.TakeOffManuallyAsync(account, 22);

        Assert.Equal(["off:22"], service.Writes);
    }

    /// <summary>明确认证失败会撤销旧待确认状态，同Cookie重新验证后也不会复活。</summary>
    [Fact]
    public async Task AuthenticationFailureClearsPendingChange()
    {
        var service = new FakeMedalService();
        var old = service.Collection;
        service.ReadResponse = _ => old;
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        using var log = new AppLogService();
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);

        Assert.True((await coordinator.WearManuallyAsync(account, 11)).IsWearingStatePending);
        service.ReadError = new BilibiliAuthenticationException("expired");
        await Assert.ThrowsAsync<BilibiliAuthenticationException>(() => coordinator.RefreshAsync(account));
        await sessions.ValidateAsync(account);
        service.ReadError = null;
        var refreshed = await coordinator.RefreshAsync(account);

        Assert.False(refreshed.IsWearingStatePending);
        Assert.Equal(22, Assert.Single(refreshed.Items, item => item.IsWearing).Id);
    }
}
