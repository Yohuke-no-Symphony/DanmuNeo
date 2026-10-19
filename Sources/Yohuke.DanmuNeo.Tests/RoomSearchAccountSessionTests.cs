using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 仅认证查找的明确未登录响应可以使绑定账号失效。
/// </summary>
public class RoomSearchAccountSessionTests
{
    /// <summary>
    /// 关注列表使用被捕获的账号 Cookie，未登录时更新共享会话状态。
    /// </summary>
    [Fact]
    public async Task FollowingAuthenticationFailureInvalidatesMatchingAccount()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        using var context = new LiveRoomSearchTestContext(sessions);
        context.SearchService.FollowingRespondAsync = () => Task.FromResult(new LiveRoomSearchPage
        {
            Issues = [new() { Source = LiveRoomSearchSource.Following, ApiCode = -101, Message = "未登录" }]
        });

        context.ViewModel.OpenAddRoomDialog(null);
        await context.ViewModel.LiveRoomSearchTask;

        Assert.Equal(AccountValidationStatus.Expired, sessions.GetState(context.ViewModel.Accounts[0].Id).Status);
    }

    /// <summary>
    /// 匿名数字查找的未登录错误不能归因到绑定账号。
    /// </summary>
    [Fact]
    public async Task AnonymousUidFailureDoesNotInvalidateAccount()
    {
        using var sessions = new BilibiliAccountSessionService(new ControlledAccountValidationService());
        using var context = new LiveRoomSearchTestContext(sessions);
        context.SearchService.RespondAsync = (_, _) => Task.FromResult(new LiveRoomSearchPage
        {
            Issues = [new() { Source = LiveRoomSearchSource.UserId, ApiCode = -101, Message = "未登录" }]
        });
        context.ViewModel.OpenAddRoomDialog(null);
        context.ViewModel.LiveRoomSearchText = "42";

        await context.ViewModel.SearchLiveRoomsAsync();

        Assert.Equal(AccountValidationStatus.Unchecked, sessions.GetState(context.ViewModel.Accounts[0].Id).Status);
    }
}
