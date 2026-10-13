using System.Net;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Views;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 浏览器登录的会话隔离与完成、关闭竞态测试。
/// </summary>
public class BrowserCookieLoginWindowTests
{
    /// <summary>
    /// B 站的 Windows 登录使用私密会话，macOS 保持非持久化会话。
    /// </summary>
    [AvaloniaFact]
    public void BilibiliLoginDoesNotUsePersistentCookies()
    {
        var window = new BrowserCookieLoginWindow();
        var windows = CreateEnvironment<WindowsWebView2EnvironmentRequestedEventArgs>();
        var apple = CreateEnvironment<AppleWKWebViewEnvironmentRequestedEventArgs>();

        try
        {
            window.ConfigureBrowserEnvironment(windows);
            window.ConfigureBrowserEnvironment(apple);

            Assert.True(windows.IsInPrivateModeEnabled);
            Assert.True(apple.NonPersistentDataStore);
            Assert.False(windows.EnableDevTools);
            Assert.False(apple.EnableDevTools);
            Assert.Equal("YohukeDanmuNeoBilibili", windows.ProfileName);
            Assert.EndsWith(Path.Combine("WebLoginProfiles", "Bilibili"), windows.UserDataFolder);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 初始化浏览器失败时保留错误占位，关闭时释放宿主。
    /// </summary>
    [AvaloniaFact]
    public async Task FailedBrowserInitializationKeepsVisibleErrorPlaceholder()
    {
        var window = CreateOfflineWindow();

        try
        {
            window.Show();
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            var placeholder = window.FindControl<TextBlock>("WebViewPlaceholderText")!;

            Assert.Same(placeholder, window.FindControl<Border>("WebViewHost")!.Child);
            Assert.True(placeholder.IsVisible);
            Assert.Contains("初始化失败", placeholder.Text);
            window.Close();
            Assert.Null(window.FindControl<Border>("WebViewHost")!.Child);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 立即关闭登录窗口后，延迟初始化不会创建浏览器。
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingBeforeDeferredOpenDoesNotCreateBrowser()
    {
        var creations = 0;
        var window = new BrowserCookieLoginWindow(BrowserLoginPlatform.Bilibili, () =>
        {
            creations++;
            return new();
        });

        try
        {
            window.Show();
            window.Close();
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, creations);
            Assert.Null(window.FindControl<Border>("WebViewHost")!.Child);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 连续完成点击只抓取一次，成功只返回一个结果。
    /// </summary>
    [AvaloniaFact]
    public async Task RepeatedCaptureReturnsOnlyOneLoginResult()
    {
        var owner = new Window();
        var window = CreateOfflineWindow();
        var response = new TaskCompletionSource<IEnumerable<Cookie>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;

        try
        {
            owner.Show();
            var resultTask = window.ShowDialog<BrowserCookieLoginResult?>(owner);
            var first = window.CaptureCookiesAsync(() =>
            {
                reads++;
                return response.Task;
            });
            await window.CaptureCookiesAsync(() =>
            {
                reads++;
                return Task.FromResult<IEnumerable<Cookie>>(LoginCookies());
            });
            Assert.False(window.FindControl<Button>("CaptureButton")!.IsEnabled);
            response.SetResult(LoginCookies());
            await first;
            var result = await resultTask;

            Assert.Equal(1, reads);
            Assert.NotNull(result);
            Assert.Equal("buvid3=;SESSDATA=test-session;bili_jct=test-csrf;DedeUserId=42", result.Cookie);
            Assert.Null(window.FindControl<Border>("WebViewHost")!.Child);
        }
        finally
        {
            window.Close();
            owner.Close();
        }
    }

    /// <summary>
    /// 取消后的迟到 Cookie 不会返回登录结果。
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingDuringCaptureRejectsLateCookies()
    {
        var owner = new Window();
        var window = CreateOfflineWindow();
        var response = new TaskCompletionSource<IEnumerable<Cookie>>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            owner.Show();
            var resultTask = window.ShowDialog<BrowserCookieLoginResult?>(owner);
            var captureTask = window.CaptureCookiesAsync(() => response.Task);
            window.Close();
            response.SetResult(LoginCookies());
            await captureTask;

            Assert.Null(await resultTask);
            Assert.Null(window.FindControl<Border>("WebViewHost")!.Child);
        }
        finally
        {
            window.Close();
            owner.Close();
        }
    }

    /// <summary>
    /// 缺少登录 Cookie 时可重新抓取，且不提前完成登录。
    /// </summary>
    [AvaloniaFact]
    public async Task FailedCaptureCanRetry()
    {
        var owner = new Window();
        var window = CreateOfflineWindow();

        try
        {
            owner.Show();
            var resultTask = window.ShowDialog<BrowserCookieLoginResult?>(owner);
            await window.CaptureCookiesAsync(() => Task.FromResult<IEnumerable<Cookie>>([]));

            Assert.False(resultTask.IsCompleted);
            Assert.True(window.FindControl<Button>("CaptureButton")!.IsEnabled);
            Assert.Contains("没有在浏览器中读取到", window.FindControl<TextBlock>("StatusText")!.Text);
            await window.CaptureCookiesAsync(() => Task.FromResult<IEnumerable<Cookie>>(LoginCookies()));
            Assert.NotNull(await resultTask);
        }
        finally
        {
            window.Close();
            owner.Close();
        }
    }

    private static BrowserCookieLoginWindow CreateOfflineWindow()
    {
        return new(BrowserLoginPlatform.Bilibili, () => throw new InvalidOperationException("离线测试不创建原生浏览器"));
    }

    private static T CreateEnvironment<T>() where T : WebViewEnvironmentRequestedEventArgs
    {
        var constructor = typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var manager = Activator.CreateInstance(constructor.GetParameters().Single().ParameterType);
        return (T)constructor.Invoke([manager]);
    }

    private static Cookie[] LoginCookies()
    {
        return [new("SESSDATA", "test-session"), new("bili_jct", "test-csrf"), new("DedeUserID", "42")];
    }
}
