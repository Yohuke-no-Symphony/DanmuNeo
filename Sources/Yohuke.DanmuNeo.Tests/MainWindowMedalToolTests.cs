using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证组合根只把活跃房间及其实际账号交给换牌协调器。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class MainWindowMedalToolTests
{
    /// <summary>实际删除账号后，跟随模式解析替代账号，固定模式清空并可重新开启跟随。</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AccountRemovalUsesFallbackOnlyWhenFollowing(bool followsCurrent)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        var application = fixture.ViewModel;
        var original = application.Accounts.Single();
        await application.AddAccountAsync();
        var fallback = application.Accounts.Single(account => account != original);
        application.AccountMedals.FollowCurrentAccount = followsCurrent;
        application.ModerationManager.FollowCurrentAccount = followsCurrent;

        await application.DeleteAccountAsync(original);

        Assert.Same(followsCurrent ? fallback : null, application.AccountMedals.SelectedAccount);
        Assert.Same(followsCurrent ? fallback : null, application.ModerationManager.SelectedAccount);
        application.AccountMedals.FollowCurrentAccount = true;
        application.ModerationManager.FollowCurrentAccount = true;
        Assert.Same(fallback, application.AccountMedals.SelectedAccount);
        Assert.Same(fallback, application.ModerationManager.SelectedAccount);
        Assert.Equal(0, fixture.ModerationService.PermissionRequestCount);
    }

    /// <summary>构造不联网；显示后才自动处理，后台房间修改不能驱动换牌。</summary>
    [AvaloniaFact]
    public async Task OnlyStartedActiveRoomAndResolvedAccountDriveAutomaticSwitching()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var first = new BilibiliAccount
        {
            Id = "first", Cookie = "SESSDATA=offline;bili_jct=csrf", IsGlobalDefault = true, AutoSwitchMedal = true
        };
        var second = new BilibiliAccount { Id = "second", Cookie = "SESSDATA=other;bili_jct=csrf" };
        var state = new AppState
        {
            Accounts = [first, second],
            Workspaces = [new() { LiveRooms = [new() { RoomId = "100" }, new() { RoomId = "200" }] }]
        };
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        var medals = new FakeMedalService();
        try
        {
            await using var application = new MainWindowViewModel(new(directory),
                avatarCacheService: new FakeRoomAvatarService(), loadedState: state,
                accountSessions: sessions, medalService: medals);
            await sessions.ValidateAsync(first);
            Assert.Equal(0, medals.OwnerReadCount);
            Assert.Equal(0, medals.ReadCount);
            Assert.Same(first, application.AccountMedals.SelectedAccount);
            Assert.Same(first, application.ModerationManager.SelectedAccount);

            await application.StartToolsAsync();
            await application.PendingMedalSwitch;
            Assert.Equal(1, medals.OwnerReadCount);
            Assert.Equal("wear:11", Assert.Single(medals.Writes));

            var workspace = application.SelectedWorkspace!;
            var current = workspace.SelectedRoom!;
            workspace.Rooms.Single(room => room != current).RoomId = "201";
            await application.PendingMedalSwitch;
            Assert.Equal(1, medals.OwnerReadCount);

            workspace.AccountOverrideId = second.Id;
            await application.PendingMedalSwitch;
            Assert.Same(second, application.AccountMedals.SelectedAccount);
            Assert.Same(second, application.ModerationManager.SelectedAccount);
            Assert.Equal(1, medals.OwnerReadCount);
            current.AccountOverrideId = first.Id;
            await application.PendingMedalSwitch;
            Assert.Same(first, application.AccountMedals.SelectedAccount);
            Assert.Same(first, application.ModerationManager.SelectedAccount);
            Assert.Equal(2, medals.OwnerReadCount);

            application.AccountMedals.FollowCurrentAccount = false;
            application.ModerationManager.FollowCurrentAccount = false;
            application.SelectedWorkspace = null;
            await application.PendingMedalSwitch;
            Assert.Same(first, application.AccountMedals.SelectedAccount);
            Assert.Same(first, application.ModerationManager.SelectedAccount);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>粉丝牌面板在无工作区选择时继续使用独立的账号上下文。</summary>
    [AvaloniaFact]
    public void AccountMedalsPanelRemainsEnabledWithoutWorkspace()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panels = new WorkbenchPanelSet(context);
        fixture.ViewModel.SelectedWorkspace = null;

        var panel = panels.Panels[WorkbenchPanelIds.ACCOUNT_MEDALS];
        Assert.True(panel.IsEnabled);
        Assert.Same(fixture.ViewModel.AccountMedals, panel.DataContext);
        Assert.Null(fixture.ViewModel.AccountMedals.SelectedAccount);
    }
}
