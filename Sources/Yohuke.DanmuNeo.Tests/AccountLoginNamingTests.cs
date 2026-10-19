using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证首次登录命名、用户标签保护及旧凭据结果隔离。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class AccountLoginNamingTests
{
    /// <summary>浏览器新增在启动验证前也会确认用户名，命名后保存且后续登录不再覆盖。</summary>
    [AvaloniaFact]
    public async Task FirstBrowserLoginNamesFromVerifiedUserAndPersistsOnce()
    {
        await using var fixture = new AccountLoginTestContext();
        Assert.Empty(fixture.Validation.Requests);
        var login = fixture.Application.ApplyBilibiliBrowserLoginAsync(Login("first"), null);
        var account = Assert.Single(fixture.Application.Accounts);
        Assert.True(account.AutoNameOnLogin);
        fixture.Validation.Requests.Single().SetResult(Valid("首次用户名"));
        await login;
        Dispatcher.UIThread.RunJobs();
        await fixture.Application.PendingAccountNameSave;

        Assert.Equal("首次用户名", account.Name);
        Assert.False(account.AutoNameOnLogin);
        Assert.Equal("首次用户名", Assert.Single(fixture.StateService.Load().Accounts).Name);
        var signingInAgain = fixture.Application.ApplyBilibiliBrowserLoginAsync(Login("second"), account);
        fixture.Validation.Requests[^1].SetResult(Valid("另一个用户名"));
        await signingInAgain;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("首次用户名", account.Name);
    }

    /// <summary>登录等待期间用户手动命名会撤销自动命名意图。</summary>
    [AvaloniaFact]
    public async Task EditingNameDuringLoginPreservesCustomLabel()
    {
        await using var fixture = new AccountLoginTestContext();
        var login = fixture.Application.ApplyBilibiliBrowserLoginAsync(Login("first"), null);
        var account = Assert.Single(fixture.Application.Accounts);
        account.Name = "我的工作账号";
        fixture.Validation.Requests.Single().SetResult(Valid("B站用户名"));
        await login;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("我的工作账号", account.Name);
        Assert.False(account.AutoNameOnLogin);
        Assert.Equal("我的工作账号", Assert.Single(fixture.StateService.Load().Accounts).Name);
    }

    /// <summary>已有配置标签和手动新增后自定义的标签均不会被登录覆盖。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingOrCustomizedAccountKeepsName(bool createManually)
    {
        await using var fixture = new AccountLoginTestContext(new BilibiliAccount { Name = "已有标签" });
        var account = fixture.Application.Accounts[0];
        if (createManually)
        {
            await fixture.Application.AddAccountAsync();
            account = fixture.Application.Accounts[^1];
            Assert.True(account.AutoNameOnLogin);
            account.Name = "已有标签";
        }
        var login = fixture.Application.ApplyBilibiliBrowserLoginAsync(Login("first"), account);
        fixture.Validation.Requests.Single().SetResult(Valid("B站用户名"));
        await login;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("已有标签", account.Name);
    }

    /// <summary>首次验证失败保留待命名意图，后续有效验证可以完成命名。</summary>
    [AvaloniaFact]
    public async Task FailedFirstValidationCanNameOnLaterSuccessfulRefresh()
    {
        await using var fixture = new AccountLoginTestContext();
        var login = fixture.Application.ApplyBilibiliBrowserLoginAsync(Login("first"), null);
        var account = Assert.Single(fixture.Application.Accounts);
        fixture.Validation.Requests.Single().SetResult(new() { Status = AccountValidationStatus.Unavailable });
        await login;
        Assert.True(account.AutoNameOnLogin);
        Assert.True(Assert.Single(fixture.StateService.Load().Accounts).AutoNameOnLogin);
        var refresh = fixture.Application.AccountItems.Single().RefreshAsync();
        fixture.Validation.Requests[^1].SetResult(Valid("恢复后的用户名"));
        await refresh;
        Dispatcher.UIThread.RunJobs();
        await fixture.Application.PendingAccountNameSave;

        Assert.Equal("恢复后的用户名", account.Name);
        Assert.False(account.AutoNameOnLogin);
    }

    /// <summary>凭据 A→B→A 后旧请求不能用相同字符串的旧身份完成命名。</summary>
    [AvaloniaFact]
    public async Task OldValidationCannotNameAfterCredentialsChangeBack()
    {
        await using var fixture = new AccountLoginTestContext();
        var login = fixture.Application.ApplyBilibiliBrowserLoginAsync(Login("first"), null);
        var account = Assert.Single(fixture.Application.Accounts);
        var row = Assert.Single(fixture.Application.AccountItems);
        account.Cookie = Login("second").Cookie;
        var second = row.RefreshAsync();
        account.Cookie = Login("first").Cookie;
        var latest = row.RefreshAsync();
        fixture.Validation.Requests[0].SetResult(Valid("旧身份"));
        await login;
        Dispatcher.UIThread.RunJobs();
        Assert.True(account.AutoNameOnLogin);
        Assert.NotEqual("旧身份", account.Name);
        fixture.Validation.Requests[2].SetResult(Valid("当前身份"));
        await latest;
        fixture.Validation.Requests[1].SetResult(Valid("中途身份"));
        await second;
        Dispatcher.UIThread.RunJobs();
        await fixture.Application.PendingAccountNameSave;

        Assert.Equal("当前身份", account.Name);
    }

    /// <summary>账号删除后迟到的登录校验不能重新添加或命名已删除账号。</summary>
    [AvaloniaFact]
    public async Task DeletedAccountCannotBeNamedByLateValidation()
    {
        await using var fixture = new AccountLoginTestContext();
        var login = fixture.Application.ApplyBilibiliBrowserLoginAsync(Login("first"), null);
        var account = Assert.Single(fixture.Application.Accounts);
        await fixture.Application.DeleteAccountAsync(account);
        fixture.Validation.Requests.Single().SetResult(Valid("迟到身份"));
        await login;
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(fixture.Application.Accounts);
        Assert.Empty(fixture.StateService.Load().Accounts);
        Assert.NotEqual("迟到身份", account.Name);
    }

    /// <summary>默认切换只选一个；重复点击当前默认和传入已删除对象都不会清空默认。</summary>
    [AvaloniaFact]
    public async Task DefaultAccountRemainsUniqueAndRejectsRemovedObjects()
    {
        await using var fixture = new AccountLoginTestContext(new() { Name = "一" }, new() { Name = "二" });
        var first = fixture.Application.Accounts[0];
        var second = fixture.Application.Accounts[1];
        await fixture.Application.SetDefaultAccountAsync(second);
        await fixture.Application.SetDefaultAccountAsync(second);
        Assert.Same(second, Assert.Single(fixture.Application.Accounts, account => account.IsGlobalDefault));
        await fixture.Application.DeleteAccountAsync(second);
        await fixture.Application.SetDefaultAccountAsync(second);
        Assert.Same(first, Assert.Single(fixture.Application.Accounts, account => account.IsGlobalDefault));
    }

    private static BrowserCookieLoginResult Login(string credential)
    {
        return new()
        {
            Platform = BrowserLoginPlatform.Bilibili,
            Cookie = $"SESSDATA=offline-{credential};bili_jct=offline"
        };
    }

    private static AccountValidationResult Valid(string name)
    {
        return new() { Status = AccountValidationStatus.Valid, Uid = 42, UserName = name, CheckedAt = DateTimeOffset.Now };
    }
}
