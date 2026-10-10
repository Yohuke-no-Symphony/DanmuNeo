using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 禁言命令的身份、取消和二次检查测试。
/// </summary>
public class RoomModerationTests
{
    /// <summary>
    /// 菜单授权后执行命令仍会再次查询，并用同一账号执行禁言。
    /// </summary>
    [Fact]
    public async Task MuteRechecksPermissionAndUsesCapturedAccount()
    {
        using var context = new RoomModerationTestContext();
        Assert.True(await context.Room.RefreshModerationPermissionAsync(context.Item));
        Assert.True(context.Room.MuteDanmuCommand.CanExecute(context.Item));
        await context.Room.MuteDanmuCommand.ExecuteAsync(context.Item);

        Assert.Equal(2, context.Service.PermissionRequestCount);
        Assert.Equal((100L, 30L, context.Account.Cookie), Assert.Single(context.Service.MuteRequests));
        Assert.Contains("已禁言", context.Room.ModerationStatus);
        Assert.False(context.Room.IsMutingDanmu);
    }

    /// <summary>
    /// 点击时失去权限不会执行写操作。
    /// </summary>
    [Fact]
    public async Task RevokedPermissionBlocksPost()
    {
        using var context = new RoomModerationTestContext();
        await context.Room.RefreshModerationPermissionAsync(context.Item);
        context.Service.RespondAsync = _ =>
            Task.FromResult(new RoomModerationPermission { RoomId = 100, AccountUid = 10, OwnerUid = 20 });
        await context.Room.MuteDanmuCommand.ExecuteAsync(context.Item);

        Assert.Empty(context.Service.MuteRequests);
        Assert.Contains("无法禁言", context.Room.ModerationStatus);
    }

    /// <summary>
    /// 权限检查期间身份、房间和生命周期变化会阻断禁言。
    /// </summary>
    [Theory]
    [InlineData("cookie")]
    [InlineData("account")]
    [InlineData("room")]
    [InlineData("selection")]
    [InlineData("stop")]
    public async Task ChangedContextBlocksPost(string change)
    {
        using var context = new RoomModerationTestContext();
        await context.Room.RefreshModerationPermissionAsync(context.Item);
        var pending = new TaskCompletionSource<RoomModerationPermission>();
        context.Service.RespondAsync = _ => pending.Task;
        var task = context.Room.MuteDanmuCommand.ExecuteAsync(context.Item);

        switch (change)
        {
            case "cookie":
                context.Account.Cookie = "SESSDATA=new-session;bili_jct=new-csrf";
                break;
            case "account":
                context.Account = new() { Id = "another-account", Cookie = context.Account.Cookie };
                break;
            case "room":
                context.Room.RoomId = "101";
                break;
            case "selection":
                context.Room.IsSelected = false;
                break;
            case "stop":
                context.Room.StopListening();
                break;
        }

        pending.SetResult(new() { RoomId = 100, AccountUid = 10, OwnerUid = 20, IsRoomAdmin = true });
        await task;
        Assert.Empty(context.Service.MuteRequests);
        Assert.DoesNotContain("已禁言", context.Room.ModerationStatus);
        Assert.False(context.Room.IsMutingDanmu);
    }

    /// <summary>
    /// 旧权限结果不能覆盖后发请求。
    /// </summary>
    [Fact]
    public async Task OldPermissionCannotOverwriteNewRequest()
    {
        using var context = new RoomModerationTestContext();
        var pending = new TaskCompletionSource<RoomModerationPermission>();
        context.Service.RespondAsync = count => count == 1
            ? pending.Task
            : Task.FromResult(new RoomModerationPermission { RoomId = 100, AccountUid = 10, OwnerUid = 20 });
        var first = context.Room.RefreshModerationPermissionAsync(context.Item);
        Assert.False(await context.Room.RefreshModerationPermissionAsync(context.Item));
        pending.SetResult(new() { RoomId = 100, AccountUid = 10, OwnerUid = 20, IsRoomAdmin = true });

        Assert.False(await first);
        Assert.False(context.Room.MuteDanmuCommand.CanExecute(context.Item));
        Assert.True(context.Service.PermissionTokens[0].IsCancellationRequested);
    }

    /// <summary>
    /// 无效弹幕、房管、主播和自己不能作为禁言目标。
    /// </summary>
    [Theory]
    [InlineData("invalid")]
    [InlineData("local")]
    [InlineData("admin")]
    [InlineData("owner")]
    [InlineData("self")]
    public async Task ProtectedTargetsCannotBeMuted(string target)
    {
        using var context = new RoomModerationTestContext();
        context.Item.UserUid = target switch { "invalid" => "0", "owner" => "20", "self" => "10", _ => "30" };
        context.Item.IsLocalRecord = target == "local";
        context.Item.IsRoomAdmin = target == "admin";

        Assert.False(await context.Room.RefreshModerationPermissionAsync(context.Item));
        Assert.False(context.Room.MuteDanmuCommand.CanExecute(context.Item));
        await context.Room.MuteDanmuAsync(context.Item);
        Assert.Empty(context.Service.MuteRequests);
    }

    /// <summary>
    /// 服务端禁言失败展示错误。
    /// </summary>
    [Fact]
    public async Task MuteFailureIsVisible()
    {
        using var context = new RoomModerationTestContext();
        context.Service.MuteException = new InvalidOperationException("目标是房管");
        await context.Room.RefreshModerationPermissionAsync(context.Item);
        await context.Room.MuteDanmuCommand.ExecuteAsync(context.Item);

        Assert.Equal("禁言失败：目标是房管", context.Room.ModerationStatus);
        Assert.False(context.Room.IsMutingDanmu);
    }

    /// <summary>
    /// 旧房间弹幕保持真实来源，不能在当前房间执行禁言。
    /// </summary>
    [Fact]
    public async Task MessageFromAnotherRoomCannotBeMuted()
    {
        using var context = new RoomModerationTestContext();
        context.Room.RoomId = "101";
        context.Room.AddDanmu(new() { RoomId = "100", Uid = 30, UserName = "旧房间用户", Content = "旧消息" });
        var item = context.Room.DanmuItems[0];

        Assert.Equal("100", item.RoomId);
        Assert.False(await context.Room.RefreshModerationPermissionAsync(item));
        Assert.Empty(context.Service.PermissionTokens);
        Assert.False(context.Room.MuteDanmuCommand.CanExecute(item));
    }
}
