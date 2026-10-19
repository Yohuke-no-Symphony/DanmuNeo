using Yohuke.DanmuNeo.I18n;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 账号登录设置页。
/// </summary>
public partial class AccountSettingsView : UserControl
{
    private readonly Func<BrowserLoginPlatform, Window, Task<BrowserCookieLoginResult?>> showBrowserLogin;
    private bool isBrowserLoginOpen;

    /// <summary>
    /// 初始化账号登录设置页。
    /// </summary>
    public AccountSettingsView()
        : this(ShowBrowserLoginDialogAsync)
    {
    }

    internal AccountSettingsView(Func<BrowserLoginPlatform, Window, Task<BrowserCookieLoginResult?>> showBrowserLogin)
    {
        this.showBrowserLogin = showBrowserLogin;
        InitializeComponent();
    }

    [RelayCommand]
    private async Task AddAccountAsync()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.AddAccountAsync();
        }
    }

    [RelayCommand]
    private async Task SetDefaultAccountAsync(BilibiliAccount? account)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.SetDefaultAccountAsync(account);
        }
    }

    [RelayCommand]
    private async Task DeleteAccountAsync(BilibiliAccount? account)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.DeleteAccountAsync(account);
        }
    }

    private async void BilibiliBrowserLogin_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var account = ((sender as Control)?.DataContext as AccountSettingsItemViewModel)?.Account;
        var result = await ShowBrowserLoginAsync(BrowserLoginPlatform.Bilibili);

        if (result is not null)
        {
            await viewModel.ApplyBilibiliBrowserLoginAsync(result, account);
        }
    }

    private async void QQMusicBrowserLogin_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var result = await ShowBrowserLoginAsync(BrowserLoginPlatform.QQMusic);

        if (result is not null)
        {
            await viewModel.ApplyQQMusicBrowserLoginAsync(result);
        }
    }

    internal async Task<BrowserCookieLoginResult?> ShowBrowserLoginAsync(BrowserLoginPlatform platform)
    {
        if (isBrowserLoginOpen || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return null;
        }

        isBrowserLoginOpen = true;

        try
        {
            return await showBrowserLogin(platform, owner);
        }
        catch (Exception exception)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SetLocalizedStatus("UI.AccountSettingsView.BrowserUnavailable", new Func<string>(() => LocalizedExceptionText.Get(exception)));
                viewModel.LogErrorOnce(Localization.Get("UI.AccountSettingsView.BrowserLogSource"), Localization.Get("UI.AccountSettingsView.BrowserLogUnavailable"), exception);
            }

            return null;
        }
        finally
        {
            isBrowserLoginOpen = false;
        }
    }

    private static async Task<BrowserCookieLoginResult?> ShowBrowserLoginDialogAsync(BrowserLoginPlatform platform, Window owner)
    {
        var window = new BrowserCookieLoginWindow(platform);

        try
        {
            return await window.ShowDialog<BrowserCookieLoginResult?>(owner);
        }
        finally
        {
            window.Close();
        }
    }
}
