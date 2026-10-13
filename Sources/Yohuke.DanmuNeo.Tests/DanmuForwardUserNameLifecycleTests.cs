using Avalonia.Headless.XUnit;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 工作区与规则生命周期的用户名查询集成测试。
/// </summary>
public class DanmuForwardUserNameLifecycleTests
{
    /// <summary>
    /// 重复配置不重查已有规则，新规则同样自动查询保存的 UID。
    /// </summary>
    [AvaloniaFact]
    public async Task LoadedAndAddedRulesUseInjectedLookupOnce()
    {
        using var context = new DanmuForwardViewTestContext();
        await context.Room.ForwardRules[0].UserNameLookupTask;
        Assert.Equal([42L], context.UserNameService.RequestedUids);

        await context.Room.AddForwardRuleStateAsync(new() { SenderUid = "84" });
        await context.Room.ForwardRules[1].UserNameLookupTask;

        Assert.Equal([42L, 84L], context.UserNameService.RequestedUids);
        Assert.Equal("用户84", context.Room.ForwardRules[1].SenderUserName);
    }

    /// <summary>
    /// 删除规则、房间、工作区或关闭应用会取消查询，拒绝迟到结果。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("rule")]
    [InlineData("room")]
    [InlineData("workspace")]
    [InlineData("app")]
    public async Task RemovingOwnerCancelsUserNameLookup(string owner)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeBilibiliUserNameService
        {
            RespondAsync = (_, _) =>
            {
                started.SetResult();
                return response.Task;
            }
        };
        using var context = new DanmuForwardViewTestContext(service);
        var rule = context.Room.ForwardRules[0];
        var lookupTask = rule.UserNameLookupTask;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        switch (owner)
        {
            case "rule":
                await context.Room.RemoveForwardRuleAsync(rule);
                break;
            case "room":
                await context.ViewModel.RemoveRoomAsync(context.Room);
                break;
            case "workspace":
                var workspace = context.ViewModel.Workspaces[0];
                await context.ViewModel.AddWorkspaceAsync();
                await context.ViewModel.DeleteWorkspaceAsync(workspace);
                break;
            case "app":
                context.ViewModel.Dispose();
                break;
        }

        Assert.True(Assert.Single(service.RequestTokens).IsCancellationRequested);
        response.SetResult("删除后迟到的名字");
        await lookupTask;
        Assert.Equal("", rule.SenderUserName);
    }
}
