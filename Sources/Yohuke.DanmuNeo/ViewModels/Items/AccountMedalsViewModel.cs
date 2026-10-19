using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>独立于房间标签页的账号粉丝牌管理。</summary>
public partial class AccountMedalsViewModel : ObservableObject, IDisposable
{
    private readonly MedalSwitchCoordinator coordinator;
    private readonly IAppLogService log;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource selection = new();
    private CancellationTokenSource refresh = new();
    private bool isActive;
    private bool isDirty = true;
    private BilibiliAccount? currentAccount;
    private long selectionRevision;
    private long operationRevision;
    private bool currentOperationReadOnly;
    private string statusKey = "Medals.ChooseAccount";
    private string errorDetail = "";
    private bool disposed;

    [ObservableProperty] private bool followCurrentAccount = true;
    [ObservableProperty] private BilibiliAccount? selectedAccount;
    [ObservableProperty] private AccountMedal? selectedMedal;
    [ObservableProperty] private bool isBusy;

    /// <summary>构造账号工具；不读取真实凭据或主动联网。</summary>
    public AccountMedalsViewModel(ObservableCollection<BilibiliAccount> accounts,
        MedalSwitchCoordinator coordinator, IAppLogService log)
    {
        Accounts = accounts;
        this.coordinator = coordinator;
        this.log = log;
        accounts.CollectionChanged += OnAccountsChanged;
        coordinator.StateChanged += OnMedalsChanged;
        Localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>可显式选择的应用账号。</summary>
    public ObservableCollection<BilibiliAccount> Accounts { get; }
    /// <summary>当前选择账号的运行时牌墙。</summary>
    public ObservableCollection<AccountMedal> Medals { get; } = [];
    /// <summary>固定账号模式允许选择任意已添加账号。</summary>
    public bool CanChooseAccount => !FollowCurrentAccount;
    /// <summary>是否可主动刷新。</summary>
    public bool CanRefresh => isActive && !IsBusy && SelectedAccount is not null;
    /// <summary>是否可手动佩戴当前选择。</summary>
    public bool CanWear => CanRefresh && SelectedMedal is not null;
    /// <summary>是否可摘下当前佩戴的牌子。</summary>
    public bool CanTakeOff => CanRefresh && Medals.Any(item => item.IsWearing);
    /// <summary>已选择账号的自动偏好；不属于房间配置。</summary>
    public bool AutoSwitchMedal
    {
        get => SelectedAccount?.AutoSwitchMedal == true;
        set
        {
            if (SelectedAccount is null || value == SelectedAccount.AutoSwitchMedal)
            {
                return;
            }
            PendingOperation = RunOperationAsync(async (account, _) =>
            {
                await coordinator.SetAutomaticAsync(account, value);
                return null;
            });
        }
    }
    /// <summary>不会暴露凭据的操作状态。</summary>
    public string StatusText => statusKey == "Medals.Failed"
        ? Localization.Format(statusKey, errorDetail)
        : statusKey == "Medals.Loaded" ? Localization.Format(statusKey, Medals.Count) : Localization.Get(statusKey);

    internal Task PendingOperation { get; private set; } = Task.CompletedTask;

    /// <summary>面板实际可见后才刷新脏数据，构造或隐藏状态不触发牌墙读取。</summary>
    public Task ActivateAsync()
    {
        if (disposed)
        {
            return Task.CompletedTask;
        }
        isActive = true;
        NotifyCommands();
        return isDirty && CanRefresh ? RefreshAsync() : PendingOperation;
    }

    /// <summary>隐藏时撤销面板的只读刷新，保留缓存并保持自动协调器独立运行。</summary>
    public void Deactivate()
    {
        if (disposed || !isActive)
        {
            return;
        }
        isActive = false;
        isDirty = true;
        refresh.Cancel();
        refresh.Dispose();
        refresh = new();
        if (IsBusy && currentOperationReadOnly)
        {
            operationRevision++;
            IsBusy = false;
            SetStatus("Medals.RefreshRequired");
        }
        NotifyCommands();
    }

    /// <summary>由组合根更新当前房间解析到的账号，固定账号模式不随房间变化。</summary>
    public void UpdateCurrentAccount(BilibiliAccount? account)
    {
        currentAccount = account;
        if (FollowCurrentAccount)
        {
            SelectedAccount = account;
        }
    }

    /// <summary>主动查询账号牌墙。</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public Task RefreshAsync()
    {
        return PendingOperation = RunOperationAsync((account, token) =>
            RefreshCollectionAsync(account, token), true);
    }

    /// <summary>手动佩戴并先关闭该账号的自动换牌。</summary>
    [RelayCommand(CanExecute = nameof(CanWear))]
    public Task WearSelectedAsync()
    {
        if (SelectedMedal is not { } medal)
        {
            return Task.CompletedTask;
        }
        return PendingOperation = RunOperationAsync(async (account, token) =>
            await coordinator.WearManuallyAsync(account, medal.Id, token));
    }

    /// <summary>手动摘牌并先关闭该账号的自动换牌。</summary>
    [RelayCommand(CanExecute = nameof(CanTakeOff))]
    public Task TakeOffAsync()
    {
        return PendingOperation = RunOperationAsync(async (account, token) =>
            await coordinator.TakeOffManuallyAsync(account, token));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        selection.Cancel();
        selection.Dispose();
        refresh.Cancel();
        refresh.Dispose();
        lifetime.Cancel();
        lifetime.Dispose();
        Accounts.CollectionChanged -= OnAccountsChanged;
        coordinator.StateChanged -= OnMedalsChanged;
        Localization.LanguageChanged -= OnLanguageChanged;
        if (SelectedAccount is not null)
        {
            SelectedAccount.PropertyChanged -= OnSelectedAccountPropertyChanged;
        }
    }

    partial void OnFollowCurrentAccountChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChooseAccount));
        if (value)
        {
            SelectedAccount = currentAccount;
        }
    }

    partial void OnSelectedAccountChanged(BilibiliAccount? oldValue, BilibiliAccount? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedAccountPropertyChanged;
        }
        CancelSelection();
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedAccountPropertyChanged;
        }
        Medals.Clear();
        SelectedMedal = null;
        isDirty = true;
        SetStatus("Medals.ChooseAccount");
        OnPropertyChanged(nameof(AutoSwitchMedal));
        NotifyCommands();
        if (newValue is not null && isActive)
        {
            PendingOperation = RefreshAsync();
        }
    }

    partial void OnSelectedMedalChanged(AccountMedal? value) => NotifyCommands();
    partial void OnIsBusyChanged(bool value) => NotifyCommands();

    private async Task<AccountMedalCollection?> RefreshCollectionAsync(BilibiliAccount account, CancellationToken token)
    {
        return await coordinator.RefreshAsync(account, token);
    }

    private async Task RunOperationAsync(Func<BilibiliAccount, CancellationToken, Task<AccountMedalCollection?>> operation,
        bool readOnly = false)
    {
        if (disposed || SelectedAccount is not { } account)
        {
            return;
        }
        var revision = selectionRevision;
        var operationId = ++operationRevision;
        currentOperationReadOnly = readOnly;
        var cookie = account.Cookie;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(selection.Token,
            readOnly ? refresh.Token : CancellationToken.None);
        var cancellationToken = cancellation.Token;
        IsBusy = true;
        SetStatus("Medals.Loading");
        try
        {
            var collection = await operation(account, cancellationToken);
            if (IsCurrent(account, cookie, revision, operationId) && collection is not null)
            {
                Apply(collection);
            }
            else if (IsCurrent(account, cookie, revision, operationId))
            {
                SetStatus("Medals.PreferenceSaved");
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(account, cookie, revision, operationId))
            {
                SetStatus(readOnly && !isActive ? "Medals.RefreshRequired" : "Medals.AccountInvalid");
            }
        }
        catch (Exception exception)
        {
            if (IsCurrent(account, cookie, revision, operationId))
            {
                errorDetail = AppLogSanitizer.Sanitize(LocalizedExceptionText.Get(exception));
                SetStatus("Medals.Failed");
                log.Error(Localization.Get("Medals.Source"), StatusText, exception);
            }
        }
        finally
        {
            if (IsCurrent(account, cookie, revision, operationId))
            {
                IsBusy = false;
                OnPropertyChanged(nameof(AutoSwitchMedal));
            }
        }
    }

    private void Apply(AccountMedalCollection collection)
    {
        isDirty = false;
        var previousId = SelectedMedal?.Id;
        Medals.Clear();
        foreach (var medal in collection.Items)
        {
            Medals.Add(medal);
        }
        SelectedMedal = Medals.FirstOrDefault(item => item.Id == previousId);
        SetStatus(collection.IsComplete ? "Medals.Loaded" : "Medals.Incomplete");
        NotifyCommands();
    }

    private void OnMedalsChanged(object? sender, AccountMedalSnapshot snapshot)
    {
        var revision = selectionRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && revision == selectionRevision && SelectedAccount?.Id == snapshot.AccountId &&
                !snapshot.CredentialCancellationToken.IsCancellationRequested)
            {
                Apply(snapshot.Collection);
            }
        });
    }

    private void OnAccountsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedAccount is not null && !Accounts.Contains(SelectedAccount))
        {
            SelectedAccount = null;
        }
        if (currentAccount is not null && !Accounts.Contains(currentAccount))
        {
            currentAccount = null;
        }
    }

    private void OnSelectedAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed || !ReferenceEquals(sender, SelectedAccount))
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnSelectedAccountPropertyChanged(sender, e));
            return;
        }

        if (e.PropertyName == nameof(BilibiliAccount.AutoSwitchMedal))
        {
            OnPropertyChanged(nameof(AutoSwitchMedal));
        }
        else if (e.PropertyName == nameof(BilibiliAccount.Cookie))
        {
            CancelSelection();
            Medals.Clear();
            SelectedMedal = null;
            isDirty = true;
            SetStatus("Medals.RefreshRequired");
            NotifyCommands();
        }
    }

    private void CancelSelection()
    {
        selectionRevision++;
        selection.Cancel();
        selection.Dispose();
        selection = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        IsBusy = false;
    }

    private bool IsCurrent(BilibiliAccount account, string cookie, long revision, long operationId)
    {
        return !disposed && selectionRevision == revision && operationRevision == operationId &&
            ReferenceEquals(SelectedAccount, account) &&
            account.Cookie == cookie;
    }

    private void SetStatus(string key)
    {
        statusKey = key;
        OnPropertyChanged(nameof(StatusText));
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanWear));
        OnPropertyChanged(nameof(CanTakeOff));
        RefreshCommand.NotifyCanExecuteChanged();
        WearSelectedCommand.NotifyCanExecuteChanged();
        TakeOffCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(StatusText));
}
