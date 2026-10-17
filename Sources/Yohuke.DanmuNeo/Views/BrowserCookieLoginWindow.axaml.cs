using System.Net;
using Avalonia.Controls;
using Avalonia.Platform;
using Yohuke.DanmuNeo.I18n;
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
    private string statusKey = "UI.BrowserCookieLoginWindow.SignInHint";
    private object[] statusArguments = [];
    private string? rawStatus;
    private bool browserInitializationFailed;

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
        Localization.LanguageChanged += OnLanguageChanged;
        ConfigureShell();
    }

    private void ConfigureShell()
    {
        var platformName = loginService.GetPlatformName(platform);

        Title = Localization.Format("UI.BrowserCookieLoginWindow.PlatformTitle", platformName);
        HeaderText.Text = Title;
        StatusText.Text = rawStatus ?? RenderStatus();
        WebViewPlaceholderText.Text = Localization.Get(browserInitializationFailed
            ? "UI.BrowserCookieLoginWindow.InitializationFailed"
            : "UI.BrowserCookieLoginWindow.PreparingBrowser");
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ConfigureShell();
    }

    private void SetLocalizedStatus(string key, params object[] arguments)
    {
        statusKey = key;
        statusArguments = arguments;
        rawStatus = null;
        StatusText.Text = RenderStatus();
    }

    private string RenderStatus()
    {
        return Localization.Format(statusKey, statusArguments.Select(argument =>
            argument is Func<string> provider ? provider() : argument).ToArray());
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
        Localization.LanguageChanged -= OnLanguageChanged;
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
                StartupLog.Append(Localization.Format("UI.BrowserCookieLoginWindow.StopFailed", platform), AppLogLevel.ERROR, Localization.Get("UI.BrowserCookieLoginWindow.LogSource"), exception);
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
            StartupLog.Append(Localization.Format("UI.BrowserCookieLoginWindow.BrowserCreated", platform));
        }
        catch (Exception exception)
        {
            ReleaseBrowser();
            browserInitializationFailed = true;
            SetLocalizedStatus("UI.BrowserCookieLoginWindow.InitializationFailedWithDetail", new Func<string>(() => LocalizedExceptionText.Get(exception)));
            WebViewPlaceholderText.Text = Localization.Get("UI.BrowserCookieLoginWindow.InitializationFailed");
            WebViewHost.Child = WebViewPlaceholderText;
            WebViewPlaceholderText.IsVisible = true;
            StartupLog.Append(Localization.Format("UI.BrowserCookieLoginWindow.InitializeLogFailed", platform), AppLogLevel.ERROR, Localization.Get("UI.BrowserCookieLoginWindow.LogSource"), exception);
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
            rawStatus = webView?.Source.ToString() ?? loginService.GetLoginUri(platform).ToString();
            StatusText.Text = rawStatus;
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
                SetLocalizedStatus("UI.BrowserCookieLoginWindow.CaptureFailed", new Func<string>(() => LocalizedExceptionText.Get(exception)));
                StartupLog.Append(Localization.Format("UI.BrowserCookieLoginWindow.ReadSignInLogFailed", platform), AppLogLevel.ERROR, Localization.Get("UI.BrowserCookieLoginWindow.LogSource"), exception);
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
            throw LocalizedExceptionText.Create("UI.BrowserCookieLoginWindow.BrowserNotReady",
                message => new InvalidOperationException(message));
        }

        var cookieManager = currentWebView.TryGetCookieManager()
                            ?? throw LocalizedExceptionText.Create("UI.BrowserCookieLoginWindow.CookieReadingUnsupported",
                                message => new InvalidOperationException(message));
        return await cookieManager.GetCookiesAsync();
    }
}
