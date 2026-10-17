using System.Globalization;
using Avalonia.Threading;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class DanmuForwardRuleViewModel
{
    private const int USER_NAME_LOOKUP_DELAY_MS = 300;
    private IBilibiliUserNameService? userNameService;
    private IAppLogService? log;
    private Func<string>? getLogSource;
    private Func<TimeSpan?>? getUserNameTimeout;
    private TimeSpan userNameLookupDelay = TimeSpan.FromMilliseconds(USER_NAME_LOOKUP_DELAY_MS);
    private CancellationTokenSource? userNameTokenSource;
    private string senderUserName = "";
    private LocalizedMessage? senderUserNameMessage;
    private bool isDisposed;

    /// <summary>
    /// 监听发送人的公开用户名。
    /// </summary>
    public string SenderUserName => senderUserName;

    /// <summary>
    /// UID 旁显示的用户名或查询状态。
    /// </summary>
    public string SenderUserNameDisplayText => senderUserNameMessage?.Render() ?? senderUserName;

    internal Task UserNameLookupTask { get; private set; } = Task.CompletedTask;

    internal void ConfigureUserNameLookup(
        IBilibiliUserNameService service,
        Func<TimeSpan?> getTimeout,
        TimeSpan? debounceDelay = null,
        IAppLogService? log = null,
        Func<string>? getLogSource = null)
    {
        var serviceChanged = !ReferenceEquals(userNameService, service);
        userNameService = service;
        getUserNameTimeout = getTimeout;
        this.log = log;
        this.getLogSource = getLogSource;
        userNameLookupDelay = debounceDelay ?? userNameLookupDelay;

        if (serviceChanged)
        {
            ScheduleUserNameLookup();
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
        Localization.LanguageChanged -= OnLanguageChanged;
        CancelUserNameLookup();
        GC.SuppressFinalize(this);
    }

    private void ScheduleUserNameLookup()
    {
        CancelUserNameLookup();
        UserNameLookupTask = Task.CompletedTask;

        if (isDisposed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SenderUid))
        {
            UpdateSenderUserName("");
            return;
        }

        if (!long.TryParse(SenderUid.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var uid) || uid <= 0)
        {
            UpdateSenderUserName("", "Forward.InvalidUid");
            return;
        }

        if (userNameService is null)
        {
            UpdateSenderUserName("");
            return;
        }

        UpdateSenderUserName("", "Forward.UserNameLoading");
        var tokenSource = new CancellationTokenSource();
        userNameTokenSource = tokenSource;
        UserNameLookupTask = LookupUserNameAsync(userNameService, uid, tokenSource);
    }

    private async Task LookupUserNameAsync(
        IBilibiliUserNameService service,
        long uid,
        CancellationTokenSource tokenSource)
    {
        var source = getLogSource?.Invoke() ?? Localization.Format("Forward.Log.UserNameSource", Id);

        try
        {
            await Task.Delay(userNameLookupDelay, tokenSource.Token);
            var name = await service.GetUserNameAsync(uid, getUserNameTimeout?.Invoke(), tokenSource.Token);
            await ApplyUserNameResultAsync(tokenSource, name);
        }
        catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!tokenSource.IsCancellationRequested && !isDisposed)
            {
                log?.Error(source, Localization.Format("Forward.Log.UserNameFailed", uid), exception);
            }

            await ApplyUserNameResultAsync(tokenSource, "", "Forward.UserNameFailed", new Func<string>(() => LocalizedExceptionText.Get(exception)));
        }
        finally
        {
            tokenSource.Dispose();
        }
    }

    private async Task ApplyUserNameResultAsync(CancellationTokenSource tokenSource, string name, string? key = null, params object[] arguments)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (isDisposed || tokenSource.IsCancellationRequested || !ReferenceEquals(userNameTokenSource, tokenSource))
            {
                return;
            }

            userNameTokenSource = null;
            UpdateSenderUserName(name, key, arguments);
        });
    }

    private void CancelUserNameLookup()
    {
        var tokenSource = userNameTokenSource;
        userNameTokenSource = null;
        tokenSource?.Cancel();
    }

    private void UpdateSenderUserName(string name, string? key = null, params object[] arguments)
    {
        SetProperty(ref senderUserName, name, nameof(SenderUserName));
        senderUserNameMessage = key is null ? null : new(key, arguments);
        OnPropertyChanged(nameof(SenderUserNameDisplayText));
    }
}
