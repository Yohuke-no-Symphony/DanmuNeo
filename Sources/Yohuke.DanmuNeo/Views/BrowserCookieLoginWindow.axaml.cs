using System.Net;
using Avalonia.Controls;
using Avalonia.Platform;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Models.Logging;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views;

/// <summary>
/// 内嵌浏览器登录窗口。
/// </summary>
public partial class BrowserCookieLoginWindow : Window
{
    private readonly BrowserLoginPlatform platform;
    private readonly BrowserCookieLoginService loginService = new();
    private readonly Func<NativeWebView> createWebView;
    private NativeWebView? webView;
    private bool isClosed;
    private bool isCapturing;

    /// <summary>
    /// 初始化内嵌浏览器登录窗口。
    /// </summary>
    public BrowserCookieLoginWindow()
        : this(BrowserLoginPlatform.Bilibili)
    {
    }

    /// <summary>
    /// 初始化内嵌浏览器登录窗口。
    /// </summary>
    public BrowserCookieLoginWindow(BrowserLoginPlatform platform)
        : this(platform, () => new())
    {
    }

    internal BrowserCookieLoginWindow(BrowserLoginPlatform platform, Func<NativeWebView> createWebView)
    {
        this.platform = platform;
        this.createWebView = createWebView;
        InitializeComponent();
        WindowChrome.AttachFrame(this);
        Opened += BrowserCookieLoginWindow_OnOpened;
        Closed += BrowserCookieLoginWindow_OnClosed;
        ConfigureShell();
    }

    private void ConfigureShell()
    {
        var platformName = loginService.GetPlatformName(platform);

        Title = $"{platformName} 浏览器登录";
        HeaderText.Text = $"{platformName} 浏览器登录";
        StatusText.Text = "登录完成后点击“完成并抓取 Cookie”。Cookie 只会保存当前 API 需要的字段。";
    }

    private async void BrowserCookieLoginWindow_OnOpened(object? sender, EventArgs e)
    {
        await Task.Yield();

        if (!isClosed)
        {
            CreateBrowser();
        }
    }

    private void BrowserCookieLoginWindow_OnClosed(object? sender, EventArgs e)
    {
        isClosed = true;
        Opened -= BrowserCookieLoginWindow_OnOpened;
        Closed -= BrowserCookieLoginWindow_OnClosed;
        ReleaseBrowser();
    }

    private void ReleaseBrowser()
    {
        var currentWebView = webView;
        webView = null;

        if (currentWebView is not null)
        {
            currentWebView.EnvironmentRequested -= WebView_OnEnvironmentRequested;
            currentWebView.NavigationCompleted -= WebView_OnNavigationCompleted;

            try
            {
                currentWebView.Stop();
            }
            catch (Exception exception)
            {
                StartupLog.Append($"浏览器停止失败：{platform}", AppLogLevel.ERROR, "浏览器登录", exception);
            }
        }

        WebViewHost.Child = null;
    }

    private void CreateBrowser()
    {
        try
        {
            var loginUri = loginService.GetLoginUri(platform);
            webView = createWebView();
            webView.EnvironmentRequested += WebView_OnEnvironmentRequested;
            webView.NavigationCompleted += WebView_OnNavigationCompleted;
            webView.Source = loginUri;
            WebViewHost.Child = webView;
            WebViewPlaceholderText.IsVisible = false;
            StartupLog.Append($"Browser login WebView created platform={platform}");
        }
        catch (Exception exception)
        {
            ReleaseBrowser();
            const string MESSAGE = "当前平台 WebView 初始化失败，无法使用内嵌浏览器登录。";
            StatusText.Text = $"{MESSAGE} {exception.Message}";
            WebViewPlaceholderText.Text = MESSAGE;
            WebViewHost.Child = WebViewPlaceholderText;
            WebViewPlaceholderText.IsVisible = true;
            StartupLog.Append($"浏览器初始化失败：{platform}", AppLogLevel.ERROR, "浏览器登录", exception);
        }
    }

    private void WebView_OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        ConfigureBrowserEnvironment(e);
    }

    internal void ConfigureBrowserEnvironment(WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = false;

        var profileDirectory = loginService.GetProfileDirectory(platform);
        var cacheDirectory = Path.Combine(profileDirectory, "Cache");
        Directory.CreateDirectory(profileDirectory);
        Directory.CreateDirectory(cacheDirectory);

        switch (e)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs windows:
                windows.UserDataFolder = profileDirectory;
                windows.ProfileName = $"YohukeDanmuNeo{platform}";
                windows.IsInPrivateModeEnabled = platform == BrowserLoginPlatform.Bilibili;
                break;
            case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                apple.NonPersistentDataStore = true;
                break;
            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                gtk.BaseDataDirectory = profileDirectory;
                gtk.BaseCacheDirectory = cacheDirectory;
                break;
            case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                wpe.DataDirectory = profileDirectory;
                wpe.CacheDirectory = cacheDirectory;
                break;
        }
    }

    private void WebView_OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (!isClosed)
        {
            StatusText.Text = webView?.Source.ToString() ?? loginService.GetLoginUri(platform).ToString();
        }
    }

    private void Back_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        webView?.GoBack();
    }

    private void Forward_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        webView?.GoForward();
    }

    private void Refresh_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        webView?.Refresh();
    }

    private void Cancel_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private async void Capture_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await CaptureCookiesAsync(ReadBrowserCookiesAsync);
    }

    internal async Task CaptureCookiesAsync(Func<Task<IEnumerable<Cookie>>> readCookies)
    {
        if (isClosed || isCapturing)
        {
            return;
        }

        isCapturing = true;
        CaptureButton.IsEnabled = false;

        try
        {
            var cookies = await readCookies();

            if (isClosed)
            {
                return;
            }

            var result = loginService.CreateResult(platform, cookies);
            Close(result);
        }
        catch (Exception exception)
        {
            if (!isClosed)
            {
                StatusText.Text = exception.Message;
                StartupLog.Append($"读取登录状态失败：{platform}", AppLogLevel.ERROR, "浏览器登录", exception);
            }
        }
        finally
        {
            isCapturing = false;

            if (!isClosed)
            {
                CaptureButton.IsEnabled = true;
            }
        }
    }

    private async Task<IEnumerable<Cookie>> ReadBrowserCookiesAsync()
    {
        var currentWebView = webView;

        if (currentWebView is null)
        {
            throw new InvalidOperationException("内嵌浏览器尚未初始化完成。");
        }

        var cookieManager = currentWebView.TryGetCookieManager()
                            ?? throw new InvalidOperationException("当前平台 WebView 暂不支持读取 Cookie。");
        return await cookieManager.GetCookiesAsync();
    }
}
