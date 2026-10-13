using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 转发发送人用户名查询的状态、取消和竞态测试。
/// </summary>
public class DanmuForwardRuleViewModelTests
{
    /// <summary>
    /// 已保存的 UID 自动查询，用户名结果不触发规则变更。
    /// </summary>
    [AvaloniaFact]
    public async Task SavedUidLoadsNameWithoutChangingRule()
    {
        var service = new FakeBilibiliUserNameService();
        var changes = 0;
        using var rule = new DanmuForwardRuleViewModel(new() { SenderUid = "42" }, _ => changes++);
        rule.ConfigureUserNameLookup(service, () => null, TimeSpan.Zero);

        await rule.UserNameLookupTask;

        Assert.Equal([42L], service.RequestedUids);
        Assert.Equal("用户42", rule.SenderUserName);
        Assert.Equal("用户42", rule.SenderUserNameDisplayText);
        Assert.Equal(0, changes);
    }

    /// <summary>
    /// 连续输入只查询最后一次的完整 UID。
    /// </summary>
    [AvaloniaFact]
    public async Task RapidUidEditsQueryOnlyFinalValue()
    {
        var service = new FakeBilibiliUserNameService();
        using var rule = new DanmuForwardRuleViewModel(new(), _ => { });
        rule.ConfigureUserNameLookup(service, () => null);

        rule.SenderUid = "4";
        rule.SenderUid = "42";
        rule.SenderUid = "420";
        await rule.UserNameLookupTask;

        Assert.Equal([420L], service.RequestedUids);
        Assert.Equal("用户420", rule.SenderUserName);
    }

    /// <summary>
    /// 空白、非数字与非正数 UID 不调用接口。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-42")]
    [InlineData("+42")]
    [InlineData("9223372036854775808")]
    public async Task InvalidUidDoesNotQuery(string uid)
    {
        var service = new FakeBilibiliUserNameService();
        using var rule = new DanmuForwardRuleViewModel(new() { SenderUid = uid }, _ => { });
        rule.ConfigureUserNameLookup(service, () => null, TimeSpan.Zero);

        await rule.UserNameLookupTask;

        Assert.Empty(service.RequestedUids);
        Assert.Equal("", rule.SenderUserName);
        Assert.Equal(string.IsNullOrWhiteSpace(uid) ? "" : "UID 格式错误", rule.SenderUserNameDisplayText);
    }

    /// <summary>
    /// UID 修改清除旧名字、取消旧请求，迟到的成功或失败不能覆盖新结果。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldResultCannotReplaceCurrentName(bool oldRequestFails)
    {
        var first = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeBilibiliUserNameService
        {
            RespondAsync = (uid, _) => uid == 42 ? first.Task : current.Task
        };
        using var rule = new DanmuForwardRuleViewModel(new() { SenderUid = "42" }, _ => { });
        rule.ConfigureUserNameLookup(service, () => null, TimeSpan.Zero);
        var oldTask = rule.UserNameLookupTask;
        rule.SenderUid = "43";
        Assert.True(service.RequestTokens[0].IsCancellationRequested);
        Assert.Equal("", rule.SenderUserName);
        Assert.Equal("查询中…", rule.SenderUserNameDisplayText);
        current.SetResult("当前用户");
        await rule.UserNameLookupTask;

        if (oldRequestFails)
        {
            first.SetException(new InvalidOperationException("旧查询失败"));
        }
        else
        {
            first.SetResult("旧用户");
        }

        await oldTask;

        Assert.Equal("当前用户", rule.SenderUserNameDisplayText);
    }

    /// <summary>
    /// UID 改回原值时，也不能接受第一次查询的旧结果。
    /// </summary>
    [AvaloniaFact]
    public async Task ReturningToSameUidRejectsFirstRequest()
    {
        var first = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeBilibiliUserNameService();
        service.RespondAsync = (uid, _) => service.RequestedUids.Count == 1 ? first.Task : Task.FromResult($"最新用户{uid}");
        using var rule = new DanmuForwardRuleViewModel(new() { SenderUid = "42" }, _ => { });
        rule.ConfigureUserNameLookup(service, () => null, TimeSpan.Zero);
        var oldTask = rule.UserNameLookupTask;
        rule.SenderUid = "43";
        await rule.UserNameLookupTask;
        rule.SenderUid = "42";
        await rule.UserNameLookupTask;
        first.SetResult("过期用户42");
        await oldTask;

        Assert.Equal("最新用户42", rule.SenderUserName);
    }

    /// <summary>
    /// 查询失败只改变用户名提示，清空 UID 会移除提示。
    /// </summary>
    [AvaloniaFact]
    public async Task FailureDoesNotChangeForwardStatusAndClearingUidRemovesHint()
    {
        var service = new FakeBilibiliUserNameService
        {
            RespondAsync = (_, _) => Task.FromException<string>(new InvalidOperationException("用户不存在"))
        };
        using var rule = new DanmuForwardRuleViewModel(new() { SenderUid = "42" }, _ => { });
        rule.StatusText = "监听中";
        rule.ConfigureUserNameLookup(service, () => null, TimeSpan.Zero);
        await rule.UserNameLookupTask;

        Assert.Equal("查询失败：用户不存在", rule.SenderUserNameDisplayText);
        Assert.Equal("监听中", rule.StatusText);
        rule.SenderUid = "";
        Assert.Equal("", rule.SenderUserNameDisplayText);
    }

    /// <summary>
    /// 释放规则会取消查询，之后的迟到响应不再改变显示。
    /// </summary>
    [AvaloniaFact]
    public async Task DisposingRuleCancelsLookupAndIgnoresLateResult()
    {
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeBilibiliUserNameService { RespondAsync = (_, _) => response.Task };
        using var rule = new DanmuForwardRuleViewModel(new() { SenderUid = "42" }, _ => { });
        rule.ConfigureUserNameLookup(service, () => null, TimeSpan.Zero);
        var task = rule.UserNameLookupTask;
        rule.Dispose();
        Assert.True(Assert.Single(service.RequestTokens).IsCancellationRequested);
        response.SetResult("迟到用户");
        await task;

        Assert.Equal("", rule.SenderUserName);
    }
}
