using System.Threading;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证主窗口账号覆盖和删除操作与配置页面的集成。
/// </summary>
public class MainWindowDanmuConfigurationTests
{
    /// <summary>
    /// 全局、工作区和房间账号覆盖均刷新有效身份，相同身份不重复加载。
    /// </summary>
    [AvaloniaFact]
    public async Task AccountHierarchyRefreshesOnlyChangedIdentity()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var main = context.ViewModel;
        var room = context.Room;
        var service = context.DanmuConfigService;
        Assert.Empty(service.GetRequests);
        await room.ActivateDanmuConfigurationAsync();
        await main.AddAccountAsync();
        var first = main.Accounts[0];
        var second = main.Accounts[1];
        second.Cookie = "session-b";
        service.ResolveConfig = (_, cookie) => service.Config with
        {
            Current = cookie == "session-b" ? new() { Color = 0xFF0000, Mode = 5 } : new()
        };

        await main.SetDefaultAccountAsync(second);
        await room.DanmuConfigurationTask;
        Assert.Equal(0xFF0000, room.SelectedDanmuColor?.Value);
        await main.SetWorkspaceAccountAsync(first);
        await room.DanmuConfigurationTask;
        Assert.Equal(0xFFFFFF, room.SelectedDanmuColor?.Value);
        room.AccountOverrideId = second.Id;
        await room.DanmuConfigurationTask;
        Assert.Equal(5, room.SelectedDanmuMode?.Value);
        Assert.Equal(4, service.GetRequests.Count);

        await main.SetWorkspaceAccountAsync(second);
        room.AccountOverrideId = null;
        await main.SetDefaultAccountAsync(first);
        Assert.Equal(4, service.GetRequests.Count);
        main.SelectedWorkspace!.AccountOverrideId = first.Id;
        await room.DanmuConfigurationTask;
        Assert.Equal(5, service.GetRequests.Count);
        Assert.Equal(1, room.SelectedDanmuMode?.Value);
        Assert.Empty(service.SetRequests);
    }

    /// <summary>
    /// 新增房间不会在 Configure 前读取，激活后读取其房间和实际账号。
    /// </summary>
    [AvaloniaFact]
    public async Task NewRoomLoadsAfterConfiguration()
    {
        using var context = new LiveRoomFeedViewTestContext();
        context.ViewModel.NewRoomId = "200";
        context.ViewModel.NewRoomName = "新房间";
        await context.ViewModel.AddRoomAsync();
        var room = context.ViewModel.SelectedWorkspace!.SelectedRoom!;
        Assert.Empty(context.DanmuConfigService.GetRequests);
        await room.ActivateDanmuConfigurationAsync();
        Assert.Equal("200", Assert.Single(context.DanmuConfigService.GetRequests).RoomId);
    }

    /// <summary>
    /// 删除房间、工作区或关闭应用都显式取消请求并停止观察账号。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("room")]
    [InlineData("workspace")]
    [InlineData("dispose")]
    public async Task RemovalAndDisposalCancelOutstandingReads(string operation)
    {
        using var context = new LiveRoomFeedViewTestContext();
        var workspace = context.ViewModel.Workspaces[0];

        if (operation == "workspace")
        {
            context.ViewModel.NewWorkspaceName = "保留的工作区";
            await context.ViewModel.AddWorkspaceAsync();
        }

        var completion = new TaskCompletionSource<DanmuRoomConfig>();
        var token = CancellationToken.None;
        context.DanmuConfigService.ReadAsync = (_, _, cancellationToken, _) =>
        {
            token = cancellationToken;
            return completion.Task;
        };
        var task = context.Room.ActivateDanmuConfigurationAsync();

        switch (operation)
        {
            case "room":
                await context.ViewModel.RemoveRoomAsync(context.Room);
                break;
            case "workspace":
                await context.ViewModel.DeleteWorkspaceAsync(workspace);
                break;
            default:
                context.ViewModel.Dispose();
                break;
        }

        Assert.True(token.IsCancellationRequested);
        context.ViewModel.Accounts[0].Cookie = "session-b";
        completion.SetResult(context.DanmuConfigService.Config);
        await task;
        Assert.Single(context.DanmuConfigService.GetRequests);
        Assert.Empty(context.Room.DanmuColorOptions);
        Assert.False(context.Room.CanSelectDanmuColor);
    }
}
