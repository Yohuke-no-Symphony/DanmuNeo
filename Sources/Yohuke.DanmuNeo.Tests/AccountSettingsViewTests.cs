using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 账号设置页浏览器登录的重复打开保护测试。
/// </summary>
public class AccountSettingsViewTests
{
    /// <summary>
    /// 同一设置页只允许一个待完成登录窗口，完成后可以重新登录。
    /// </summary>
    [AvaloniaFact]
    public async Task RepeatedLoginOpensOnlyOneDialogAndAllowsNextSession()
    {
        var response = new TaskCompletionSource<BrowserCookieLoginResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opens = 0;
        var view = new AccountSettingsView((_, _) =>
        {
            opens++;
            return response.Task;
        });
        var window = new Window { Content = view };

        try
        {
            window.Show();
            var first = view.ShowBrowserLoginAsync(BrowserLoginPlatform.Bilibili);
            Assert.Null(await view.ShowBrowserLoginAsync(BrowserLoginPlatform.Bilibili));
            Assert.Null(await view.ShowBrowserLoginAsync(BrowserLoginPlatform.QQMusic));
            Assert.Equal(1, opens);
            var result = new BrowserCookieLoginResult { Platform = BrowserLoginPlatform.Bilibili, Cookie = "test-cookie" };
            response.SetResult(result);
            Assert.Same(result, await first);

            Assert.Same(result, await view.ShowBrowserLoginAsync(BrowserLoginPlatform.Bilibili));
            Assert.Equal(2, opens);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 窗口初始化失败后仍可再次打开，不会永久锁住登录入口。
    /// </summary>
    [AvaloniaFact]
    public async Task FailedDialogDoesNotBlockRetry()
    {
        var opens = 0;
        var view = new AccountSettingsView((_, _) =>
        {
            opens++;
            return Task.FromException<BrowserCookieLoginResult?>(new InvalidOperationException("离线初始化失败"));
        });
        var window = new Window { Content = view };

        try
        {
            window.Show();
            Assert.Null(await view.ShowBrowserLoginAsync(BrowserLoginPlatform.Bilibili));
            Assert.Null(await view.ShowBrowserLoginAsync(BrowserLoginPlatform.Bilibili));
            Assert.Equal(2, opens);
        }
        finally
        {
            window.Close();
        }
    }
}
