using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 主窗口视图模型测试。
/// </summary>
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
}
