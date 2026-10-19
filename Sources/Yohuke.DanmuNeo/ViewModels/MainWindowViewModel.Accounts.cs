using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.ViewModels;

public partial class MainWindowViewModel
{
    private readonly IBilibiliAccountSessionService accountSessions;
    private readonly bool ownsAccountSessions;
    private bool accountValidationStarted;
    private CancellationTokenSource? accountContextRefreshTokenSource;

    /// <summary>
    /// 账号设置行，包括仅存在于本次运行的验证结果。
    /// </summary>
    public ObservableCollection<AccountSettingsItemViewModel> AccountItems { get; } = [];

    private void InitializeAccounts()
    {
        foreach (var account in Accounts)
        {
            AddAccountItem(account);
        }

        Accounts.CollectionChanged += OnAccountsCollectionChanged;
    }

    private void AddAccountItem(BilibiliAccount account)
    {
        accountSessions.Register(account);
        var item = new AccountSettingsItemViewModel(account, accountSessions,
            () => TimeSpan.FromSeconds(Math.Max(0.1, Settings.TimeoutSeconds)));
        AccountItems.Add(item);
        account.PropertyChanged += OnStoredAccountCredentialsChanged;
        if (accountValidationStarted)
        {
            item.StartAutomaticValidation();
        }
    }

    private void OnAccountsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in AccountItems.Where(item => !Accounts.Contains(item.Account)).ToArray())
        {
            item.Dispose();
            item.Account.PropertyChanged -= OnStoredAccountCredentialsChanged;
            accountSessions.Remove(item.Account.Id);
            AccountItems.Remove(item);
        }

        foreach (var account in Accounts.Where(account => AccountItems.All(item => item.Account != account)))
        {
            AddAccountItem(account);
        }
    }

    private void OnStoredAccountCredentialsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BilibiliAccount.Cookie) || isDisposed)
        {
            return;
        }

        accountContextRefreshTokenSource?.Cancel();
        accountContextRefreshTokenSource?.Dispose();
        accountContextRefreshTokenSource = new();
        _ = RefreshAccountContextsAfterEditAsync(accountContextRefreshTokenSource.Token);
    }

    private async Task RefreshAccountContextsAfterEditAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (!isDisposed && !cancellationToken.IsCancellationRequested)
                {
                    RefreshAccountLabels();
                    RestartForwarding();
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// 主窗口显示后开始首次验证并启用后续编辑验证。
    /// </summary>
    public async Task StartAccountValidationAsync()
    {
        if (accountValidationStarted || isDisposed)
        {
            return;
        }

        accountValidationStarted = true;
        foreach (var item in AccountItems)
        {
            item.StartAutomaticValidation();
        }

        await Task.WhenAll(AccountItems.Select(item => item.RefreshAsync()));
    }

    private void DisposeAccounts()
    {
        accountContextRefreshTokenSource?.Cancel();
        accountContextRefreshTokenSource?.Dispose();
        Accounts.CollectionChanged -= OnAccountsCollectionChanged;
        foreach (var item in AccountItems)
        {
            item.Dispose();
            item.Account.PropertyChanged -= OnStoredAccountCredentialsChanged;
            accountSessions.Remove(item.Account.Id);
        }

        AccountItems.Clear();
        if (ownsAccountSessions)
        {
            accountSessions.Dispose();
        }
    }

    /// <summary>
    /// 新增账号。
    /// </summary>
    public async Task AddAccountAsync()
    {
        var account = new BilibiliAccount
        {
            Name = Localization.Format("App.Default.Account", Accounts.Count + 1),
            IsGlobalDefault = Accounts.Count == 0
        };
        Accounts.Add(account);
        State.Accounts.Add(account);
        RefreshAccountLabels();
        RefreshForwardAccountOptions();
        await SaveAsync();
    }

    /// <summary>
    /// 应用 B 站浏览器登录结果。
    /// </summary>
    public async Task ApplyBilibiliBrowserLoginAsync(BrowserCookieLoginResult result, BilibiliAccount? account)
    {
        if (result.Platform != BrowserLoginPlatform.Bilibili || string.IsNullOrWhiteSpace(result.Cookie))
        {
            return;
        }

        var targetAccount = account;

        if (targetAccount is null)
        {
            targetAccount = new()
            {
                Name = Localization.Format("App.Default.BilibiliAccount", Accounts.Count + 1),
                IsGlobalDefault = Accounts.Count == 0
            };
            Accounts.Add(targetAccount);
            State.Accounts.Add(targetAccount);
            RefreshForwardAccountOptions();
        }

        targetAccount.Cookie = result.Cookie;
        if (accountValidationStarted && AccountItems.FirstOrDefault(item => item.Account == targetAccount) is { } accountItem)
        {
            await accountItem.RefreshAsync();
        }
        RefreshAccountLabels();
        await SaveAsync();
        SetBrowserLoginStatus(result);
        Log.Information(Localization.Get("App.Log.BilibiliLogin"), StatusMessage);
    }

    /// <summary>
    /// 应用 QQ 音乐浏览器登录结果。
    /// </summary>
    public async Task ApplyQQMusicBrowserLoginAsync(BrowserCookieLoginResult result)
    {
        if (result.Platform != BrowserLoginPlatform.QQMusic || string.IsNullOrWhiteSpace(result.Cookie))
        {
            return;
        }

        Settings.QQMusicCookie = result.Cookie;
        OnPropertyChanged(nameof(QQMusicCookieStatus));
        OnPropertyChanged(nameof(QQMusicCookieSummary));
        await SaveAsync();
        SetBrowserLoginStatus(result);
        Log.Information(Localization.Get("App.Log.QQMusicLogin"), StatusMessage);
    }

    /// <summary>
    /// 删除账号。
    /// </summary>
    public async Task DeleteAccountAsync(BilibiliAccount? account)
    {
        if (account is null)
        {
            return;
        }

        Accounts.Remove(account);
        State.Accounts.Remove(account);

        foreach (var workspace in State.Workspaces)
        {
            if (workspace.AccountOverrideId == account.Id)
            {
                workspace.AccountOverrideId = null;
            }

            foreach (var room in workspace.LiveRooms)
            {
                if (room.AccountOverrideId == account.Id)
                {
                    room.AccountOverrideId = null;
                }

            }
        }

        foreach (var rule in Workspaces
                     .SelectMany(workspace => workspace.Rooms)
                     .SelectMany(room => room.ForwardRules)
                     .Where(rule => rule.AccountOverrideId == account.Id))
        {
            rule.AccountOverrideId = "";
        }

        if (Accounts.Count > 0 && Accounts.All(item => !item.IsGlobalDefault))
        {
            Accounts[0].IsGlobalDefault = true;
        }

        RefreshAccountLabels();
        RefreshForwardAccountOptions();
        await SaveAsync();
    }

    /// <summary>
    /// 设置默认账号。
    /// </summary>
    public async Task SetDefaultAccountAsync(BilibiliAccount? account)
    {
        if (account is null)
        {
            return;
        }

        foreach (var item in Accounts)
        {
            item.IsGlobalDefault = item == account;
        }

        RefreshAccountLabels();
        await SaveAsync();
    }

}
