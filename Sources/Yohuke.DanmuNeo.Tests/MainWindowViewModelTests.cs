using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 主窗口视图模型测试。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class MainWindowViewModelTests
{
    [Fact]
    public void CompactDanmuDisplayUpdatesSettingsAndRaisesNotification()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        service.Save(new());
        using var viewModel = new MainWindowViewModel(service, avatarCacheService: new FakeRoomAvatarService());
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        viewModel.IsCompactDanmuDisplay = true;

        Assert.True(viewModel.Settings.CompactDanmuDisplay);
        Assert.Contains(nameof(MainWindowViewModel.IsCompactDanmuDisplay), changedProperties);
    }

    [Fact]
    public void HideEmoticonDanmuUpdatesSettingsAndRaisesNotification()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        service.Save(new());
        using var viewModel = new MainWindowViewModel(service, avatarCacheService: new FakeRoomAvatarService());
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        viewModel.IsHideEmoticonDanmu = true;

        Assert.True(viewModel.Settings.HideEmoticonDanmu);
        Assert.Contains(nameof(MainWindowViewModel.IsHideEmoticonDanmu), changedProperties);
    }

    [Fact]
    public async Task DeleteAccountClearsForwardRuleAccountOverride()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var account = new BilibiliAccount
        {
            Id = "forward-account",
            Name = "转发账号",
            Cookie = "cookie",
            IsGlobalDefault = true
        };
        var state = new AppState
        {
            Accounts = [account],
            Workspaces =
            [
                new()
                {
                    Name = "工作区",
                    LiveRooms =
                    [
                        new()
                        {
                            RoomId = "100",
                            RoomName = "直播间",
                            ForwardRules =
                            [
                                new()
                                {
                                    AccountOverrideId = account.Id
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        service.Save(state);
        using var viewModel = new MainWindowViewModel(service, avatarCacheService: new FakeRoomAvatarService());

        await viewModel.DeleteAccountAsync(viewModel.Accounts.Single());

        Assert.Equal("", viewModel.Workspaces[0].Rooms[0].State.ForwardRules[0].AccountOverrideId);
    }

    /// <summary>
    /// 普通或浏览器新增账号改名后，转发选项立即同步且已有选项对象不变。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewlyAddedAccountNameUpdatesForwardOption(bool browserLogin)
    {
        using var context = new DanmuForwardViewTestContext();
        var viewModel = context.ViewModel;
        var existing = viewModel.ForwardAccountOptions.ToList();

        if (browserLogin)
        {
            await viewModel.ApplyBilibiliBrowserLoginAsync(new()
            {
                Platform = BrowserLoginPlatform.Bilibili, Cookie = "test-cookie"
            }, null);
        }
        else
        {
            await viewModel.AddAccountAsync();
        }

        var account = viewModel.Accounts.Last();
        var option = viewModel.ForwardAccountOptions.Single(item => item.AccountId == account.Id);
        account.Name = "重命名的账号";

        Assert.Equal("重命名的账号", option.DisplayName);
        Assert.Same(option, viewModel.ForwardAccountOptions.Single(item => item.AccountId == account.Id));
        Assert.All(existing, item => Assert.Same(item,
            viewModel.ForwardAccountOptions.Single(current => current.AccountId == item.AccountId)));
    }

    /// <summary>
    /// 删除其他账号保留当前转发选项，并解除已删除账号的订阅。
    /// </summary>
    [AvaloniaFact]
    public async Task RemovingOtherAccountPreservesSelectedOption()
    {
        using var context = new DanmuForwardViewTestContext();
        var viewModel = context.ViewModel;
        var selected = viewModel.ForwardAccountOptions.Single(item => item.AccountId == "forward-account");
        var removedAccount = viewModel.Accounts.Single(account => account.Id == "default-account");
        var removedOption = viewModel.ForwardAccountOptions.Single(item => item.AccountId == removedAccount.Id);
        var notified = false;
        removedOption.PropertyChanged += (_, _) => notified = true;

        await viewModel.DeleteAccountAsync(removedAccount);
        removedAccount.Name = "已删除的账号";

        Assert.False(notified);
        Assert.Same(selected, viewModel.ForwardAccountOptions.Single(item => item.AccountId == "forward-account"));
        Assert.Equal("forward-account", context.Room.ForwardRules[0].AccountOverrideId);
    }
}
