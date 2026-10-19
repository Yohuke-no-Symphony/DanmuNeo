using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>
/// 将持久化账号与可取消、可刷新的运行时登录状态连接到设置界面。
/// </summary>
public partial class AccountSettingsItemViewModel : ObservableObject, IDisposable
{
    private readonly IBilibiliAccountSessionService sessions;
    private readonly Func<TimeSpan> getTimeout;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? debounce;
    private bool isDisposed;
    private bool automaticValidationEnabled;

    /// <summary>
    /// 初始化账号行；构造时不发起网络请求。
    /// </summary>
    public AccountSettingsItemViewModel(BilibiliAccount account, IBilibiliAccountSessionService sessions,
        Func<TimeSpan> getTimeout)
    {
        Account = account;
        this.sessions = sessions;
        this.getTimeout = getTimeout;
        account.PropertyChanged += OnAccountChanged;
        sessions.Changed += OnSessionChanged;
        Localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// 原始账号配置，保留用户定义的标签及本地引用。
    /// </summary>
    public BilibiliAccount Account { get; }

    /// <summary>
    /// 应用启动完成后允许 Cookie 编辑触发自动验证。
    /// </summary>
    public void StartAutomaticValidation()
    {
        automaticValidationEnabled = true;
    }

    /// <summary>
    /// 当前验证状态。
    /// </summary>
    public string StatusText => Localization.Get($"App.Account.Validation.{sessions.GetState(Account.Id).Status}");

    /// <summary>
    /// 服务端确认的昵称与 UID。
    /// </summary>
    public string ProfileText => sessions.GetState(Account.Id) is { Uid: > 0 } state
        ? $"{state.UserName} · UID {state.Uid}" : "";

    /// <summary>
    /// 最近一次验证的本地时间。
    /// </summary>
    public string CheckedAtText => sessions.GetState(Account.Id).CheckedAt is { } time
        ? Localization.Format("App.Account.Validation.CheckedAt", time.ToLocalTime()) : "";

    /// <summary>
    /// 主动重新验证当前凭据。
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        debounce?.Cancel();
        await ValidateAsync(lifetime.Token);
    }

    private async Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (!isDisposed)
        {
            await sessions.ValidateAsync(Account, getTimeout(), cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnAccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isDisposed || !automaticValidationEnabled || e.PropertyName != nameof(BilibiliAccount.Cookie))
        {
            return;
        }

        debounce?.Cancel();
        debounce?.Dispose();
        debounce = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = ValidateAfterEditAsync(debounce.Token);
    }

    private async Task ValidateAfterEditAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            await ValidateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void OnSessionChanged(object? sender, string accountId)
    {
        if (accountId == Account.Id)
        {
            RefreshPresentation();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshPresentation);
            return;
        }

        if (!isDisposed)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ProfileText));
            OnPropertyChanged(nameof(CheckedAtText));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        Account.PropertyChanged -= OnAccountChanged;
        sessions.Changed -= OnSessionChanged;
        Localization.LanguageChanged -= OnLanguageChanged;
        lifetime.Cancel();
        debounce?.Cancel();
        debounce?.Dispose();
        lifetime.Dispose();
    }
}
