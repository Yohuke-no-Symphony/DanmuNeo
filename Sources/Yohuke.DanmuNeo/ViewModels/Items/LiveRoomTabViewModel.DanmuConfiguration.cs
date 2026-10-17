using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class LiveRoomTabViewModel
{
    private const string DANMU_CONFIGURATION_HINT = "颜色和位置会立即保存到当前发送账号在该直播间的配置";
    private IBilibiliDanmuConfigService? danmuConfigService;
    private CancellationTokenSource? danmuConfigurationTokenSource;
    private CancellationToken danmuConfigurationCancellationToken;
    private BilibiliAccount? danmuConfigurationAccount;
    private DanmuRoomConfig? confirmedDanmuConfiguration;
    private DanmuSendOptions? pendingDanmuConfiguration;
    private string danmuConfigurationRoomId = "";
    private string danmuConfigurationAccountId = "";
    private string danmuConfigurationCookie = "";
    private long danmuConfigurationVersion;
    private bool isDanmuConfigurationActive;
    private bool suppressDanmuConfigurationSelection;

    /// <summary>
    /// 当前发送账号已解锁的弹幕颜色。
    /// </summary>
    public ObservableCollection<DanmuColorOption> DanmuColorOptions { get; } = [];

    /// <summary>
    /// 当前发送账号已解锁的弹幕位置。
    /// </summary>
    public ObservableCollection<DanmuModeOption> DanmuModeOptions { get; } = [];

    /// <summary>
    /// 当前身份的配置读取或保存任务。
    /// </summary>
    public Task DanmuConfigurationTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// 是否可以选择颜色；读取失败时保留打开下拉框重试的入口。
    /// </summary>
    public bool CanSelectDanmuColor => CanEditDanmuConfiguration &&
                                      (confirmedDanmuConfiguration is null || DanmuColorOptions.Count > 0);

    /// <summary>
    /// 是否可以选择位置；读取失败时保留打开下拉框重试的入口。
    /// </summary>
    public bool CanSelectDanmuMode => CanEditDanmuConfiguration &&
                                     (confirmedDanmuConfiguration is null || DanmuModeOptions.Count > 0);

    private bool CanEditDanmuConfiguration => isDanmuConfigurationActive && !IsLoadingDanmuConfiguration &&
                                             !string.IsNullOrWhiteSpace(danmuConfigurationCookie) &&
                                             !string.IsNullOrWhiteSpace(danmuConfigurationRoomId);

    [ObservableProperty] private DanmuColorOption? selectedDanmuColor;
    [ObservableProperty] private DanmuModeOption? selectedDanmuMode;
    [ObservableProperty] private bool isSavingDanmuConfiguration;
    [ObservableProperty] private string danmuConfigurationStatus = DANMU_CONFIGURATION_HINT;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectDanmuColor), nameof(CanSelectDanmuMode))]
    private bool isLoadingDanmuConfiguration;

    /// <summary>
    /// 在页面附着后读取当前身份的服务端配置。
    /// </summary>
    public Task ActivateDanmuConfigurationAsync()
    {
        if (isDisposed)
        {
            return Task.CompletedTask;
        }

        if (isDanmuConfigurationActive)
        {
            NotifyDanmuConfigurationAccountChanged();
            return DanmuConfigurationTask;
        }

        isDanmuConfigurationActive = true;
        return StartDanmuConfigurationLoad();
    }

    /// <summary>
    /// 关闭页面或移除房间时取消配置请求并解除账号订阅。
    /// </summary>
    public void DeactivateDanmuConfiguration()
    {
        isDanmuConfigurationActive = false;
        ResetDanmuConfigurationContext();
        RefreshDanmuConfigurationAvailability();
    }

    /// <summary>
    /// 有效发送账号变化时重新加载，身份相同时保持当前配置。
    /// </summary>
    public void NotifyDanmuConfigurationAccountChanged()
    {
        if (isDanmuConfigurationActive && !MatchesDanmuConfigurationIdentity())
        {
            StartDanmuConfigurationLoad();
        }
    }

    /// <summary>
    /// 配置读取失败后，通过重新打开下拉框重试。
    /// </summary>
    public Task RetryDanmuConfigurationAsync()
    {
        return isDanmuConfigurationActive && confirmedDanmuConfiguration is null && !IsLoadingDanmuConfiguration
            ? StartDanmuConfigurationLoad()
            : DanmuConfigurationTask;
    }

    partial void OnSelectedDanmuColorChanged(DanmuColorOption? value)
    {
        QueueDanmuConfigurationSave();
    }

    partial void OnSelectedDanmuModeChanged(DanmuModeOption? value)
    {
        QueueDanmuConfigurationSave();
    }

    private Task StartDanmuConfigurationLoad()
    {
        ResetDanmuConfigurationContext();
        danmuConfigurationAccount = resolveAccount?.Invoke(this);

        if (danmuConfigurationAccount is not null)
        {
            danmuConfigurationAccount.PropertyChanged += DanmuConfigurationAccount_OnPropertyChanged;
        }

        danmuConfigurationRoomId = RoomId;
        danmuConfigurationAccountId = danmuConfigurationAccount?.Id ?? "";
        danmuConfigurationCookie = danmuConfigurationAccount?.Cookie ?? "";

        if (danmuConfigService is null || string.IsNullOrWhiteSpace(danmuConfigurationCookie) ||
            string.IsNullOrWhiteSpace(danmuConfigurationRoomId))
        {
            DanmuConfigurationStatus = "配置发送账号的 Cookie 和直播间 ID 后可设置弹幕颜色、位置";
            RefreshDanmuConfigurationAvailability();
            return DanmuConfigurationTask = Task.CompletedTask;
        }

        danmuConfigurationTokenSource = new();
        danmuConfigurationCancellationToken = danmuConfigurationTokenSource.Token;
        IsLoadingDanmuConfiguration = true;
        DanmuConfigurationStatus = "正在读取弹幕颜色和位置";
        DanmuConfigurationTask = LoadDanmuConfigurationAsync(danmuConfigurationVersion,
            danmuConfigurationRoomId, danmuConfigurationCookie, danmuConfigurationTokenSource.Token);
        return DanmuConfigurationTask;
    }

    private async Task LoadDanmuConfigurationAsync(long version, string roomId, string cookie, CancellationToken token)
    {
        var source = CreateLogSource("弹幕配置读取", roomId);

        try
        {
            var config = await danmuConfigService!.GetConfigAsync(roomId, cookie,
                GetDanmuConfigurationTimeout(), token, forceRefresh: true);

            if (IsCurrentDanmuConfiguration(version, token))
            {
                ApplyDanmuConfiguration(config);
                DanmuConfigurationStatus = DANMU_CONFIGURATION_HINT;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentDanmuConfiguration(version, token))
            {
                log?.Error(source, "读取弹幕颜色和位置失败", exception);
                DanmuConfigurationStatus = $"读取失败：{exception.Message}；打开下拉框重试";
            }
        }
        finally
        {
            if (IsCurrentDanmuConfiguration(version, token))
            {
                IsLoadingDanmuConfiguration = false;
                RefreshDanmuConfigurationAvailability();
            }
        }
    }

    private void QueueDanmuConfigurationSave()
    {
        if (suppressDanmuConfigurationSelection || !CanEditDanmuConfiguration ||
            confirmedDanmuConfiguration is null || SelectedDanmuColor is null || SelectedDanmuMode is null ||
            !MatchesDanmuConfigurationIdentity())
        {
            return;
        }

        if (!DanmuColorOptions.Contains(SelectedDanmuColor) || !DanmuModeOptions.Contains(SelectedDanmuMode))
        {
            ApplyDanmuConfiguration(confirmedDanmuConfiguration);
            return;
        }

        var options = new DanmuSendOptions { Color = SelectedDanmuColor.Value, Mode = SelectedDanmuMode.Value };

        if (!IsSavingDanmuConfiguration && options == confirmedDanmuConfiguration.Current)
        {
            return;
        }

        pendingDanmuConfiguration = options;

        if (IsSavingDanmuConfiguration)
        {
            return;
        }

        IsSavingDanmuConfiguration = true;
        DanmuConfigurationTask = SaveDanmuConfigurationAsync(danmuConfigurationVersion,
            danmuConfigurationRoomId, danmuConfigurationCookie, danmuConfigurationTokenSource!.Token);
    }

    private async Task SaveDanmuConfigurationAsync(long version, string roomId, string cookie, CancellationToken token)
    {
        var source = CreateLogSource("弹幕配置保存", roomId);

        try
        {
            while (pendingDanmuConfiguration is { } options && IsCurrentDanmuConfiguration(version, token))
            {
                pendingDanmuConfiguration = null;
                DanmuConfigurationStatus = "正在保存弹幕颜色和位置";
                var config = await danmuConfigService!.SetConfigAsync(roomId, cookie, options,
                    GetDanmuConfigurationTimeout(), token);

                if (!IsCurrentDanmuConfiguration(version, token))
                {
                    return;
                }

                if (pendingDanmuConfiguration == config.Current)
                {
                    pendingDanmuConfiguration = null;
                }

                ApplyDanmuConfiguration(config, pendingDanmuConfiguration is not null);
            }

            if (IsCurrentDanmuConfiguration(version, token))
            {
                DanmuConfigurationStatus = DANMU_CONFIGURATION_HINT;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentDanmuConfiguration(version, token))
            {
                log?.Error(source, "保存弹幕颜色和位置失败", exception);
                pendingDanmuConfiguration = null;
                await RecoverDanmuConfigurationAsync(version, roomId, cookie, token, exception.Message);
            }
        }
        finally
        {
            if (IsCurrentDanmuConfiguration(version, token))
            {
                IsSavingDanmuConfiguration = false;
            }
        }
    }

    private async Task RecoverDanmuConfigurationAsync(long version, string roomId, string cookie,
        CancellationToken token, string error)
    {
        var source = CreateLogSource("弹幕配置恢复", roomId);
        IsLoadingDanmuConfiguration = true;

        try
        {
            var config = await danmuConfigService!.GetConfigAsync(roomId, cookie, GetDanmuConfigurationTimeout(), token);

            if (IsCurrentDanmuConfiguration(version, token))
            {
                ApplyDanmuConfiguration(config);
                DanmuConfigurationStatus = $"保存失败：{error}；已恢复服务端确认的配置";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentDanmuConfiguration(version, token))
            {
                log?.Error(source, "恢复服务端弹幕配置失败", exception);
                ClearDanmuConfiguration();
                DanmuConfigurationStatus = $"保存失败：{error}；无法确认当前配置，打开下拉框重新读取";
            }
        }
        finally
        {
            if (IsCurrentDanmuConfiguration(version, token))
            {
                IsLoadingDanmuConfiguration = false;
                RefreshDanmuConfigurationAvailability();
            }
        }
    }

    private void ApplyDanmuConfiguration(DanmuRoomConfig config, bool preserveSelection = false)
    {
        suppressDanmuConfigurationSelection = true;

        try
        {
            confirmedDanmuConfiguration = config;

            if (!DanmuColorOptions.SequenceEqual(config.Colors))
            {
                DanmuColorOptions.Clear();

                foreach (var color in config.Colors)
                {
                    DanmuColorOptions.Add(color);
                }
            }

            if (!DanmuModeOptions.SequenceEqual(config.Modes))
            {
                DanmuModeOptions.Clear();

                foreach (var mode in config.Modes)
                {
                    DanmuModeOptions.Add(mode);
                }
            }

            var options = preserveSelection ? pendingDanmuConfiguration! : config.Current;
            SelectedDanmuColor = DanmuColorOptions.FirstOrDefault(color => color.Value == options.Color);
            SelectedDanmuMode = DanmuModeOptions.FirstOrDefault(mode => mode.Value == options.Mode);
        }
        finally
        {
            suppressDanmuConfigurationSelection = false;
        }

        RefreshDanmuConfigurationAvailability();
    }

    private async Task<BilibiliAccount?> PrepareDanmuSendingAccountAsync(string roomId, BilibiliAccount? account,
        CancellationToken cancellationToken)
    {
        if (account is null)
        {
            return null;
        }

        var accountId = account.Id;
        var cookie = account.Cookie;

        if (!RequiresDanmuConfigurationSaveWait(roomId, accountId, cookie))
        {
            return account;
        }

        var sendingAccount = new BilibiliAccount
        {
            Id = accountId, Name = account.Name, Cookie = cookie, IsGlobalDefault = account.IsGlobalDefault
        };

        while (RequiresDanmuConfigurationSaveWait(roomId, accountId, cookie))
        {
            var contextToken = danmuConfigurationCancellationToken;
            using var waitingTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, contextToken);

            try
            {
                await DanmuConfigurationTask.WaitAsync(waitingTokenSource.Token);
            }
            catch (OperationCanceledException) when (contextToken.IsCancellationRequested &&
                                                      !cancellationToken.IsCancellationRequested)
            {
                return sendingAccount;
            }
        }

        return sendingAccount;
    }

    private bool RequiresDanmuConfigurationSaveWait(string roomId, string accountId, string cookie)
    {
        return IsSavingDanmuConfiguration && roomId == danmuConfigurationRoomId &&
               accountId == danmuConfigurationAccountId && cookie == danmuConfigurationCookie;
    }

    private TimeSpan GetDanmuConfigurationTimeout()
    {
        return TimeSpan.FromSeconds(getSettings?.Invoke().TimeoutSeconds ?? 3.05);
    }

    private void ResetDanmuConfigurationContext()
    {
        danmuConfigurationVersion++;
        danmuConfigurationTokenSource?.Cancel();
        danmuConfigurationTokenSource?.Dispose();
        danmuConfigurationTokenSource = null;

        if (danmuConfigurationAccount is not null)
        {
            danmuConfigurationAccount.PropertyChanged -= DanmuConfigurationAccount_OnPropertyChanged;
            danmuConfigurationAccount = null;
        }

        pendingDanmuConfiguration = null;
        IsLoadingDanmuConfiguration = false;
        IsSavingDanmuConfiguration = false;
        ClearDanmuConfiguration();
    }

    private void ClearDanmuConfiguration()
    {
        suppressDanmuConfigurationSelection = true;

        try
        {
            confirmedDanmuConfiguration = null;
            DanmuColorOptions.Clear();
            DanmuModeOptions.Clear();
            SelectedDanmuColor = null;
            SelectedDanmuMode = null;
        }
        finally
        {
            suppressDanmuConfigurationSelection = false;
        }
    }

    private void DanmuConfigurationAccount_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BilibiliAccount.Cookie) or nameof(BilibiliAccount.Id))
        {
            NotifyDanmuConfigurationAccountChanged();
        }
    }

    private bool IsCurrentDanmuConfiguration(long version, CancellationToken token)
    {
        return isDanmuConfigurationActive && version == danmuConfigurationVersion &&
               !token.IsCancellationRequested && MatchesDanmuConfigurationIdentity();
    }

    private bool MatchesDanmuConfigurationIdentity()
    {
        var account = resolveAccount?.Invoke(this);
        return danmuConfigurationRoomId == RoomId && danmuConfigurationAccountId == (account?.Id ?? "") &&
               danmuConfigurationCookie == (account?.Cookie ?? "");
    }

    private void RefreshDanmuConfigurationAvailability()
    {
        OnPropertyChanged(nameof(CanSelectDanmuColor));
        OnPropertyChanged(nameof(CanSelectDanmuMode));
    }
}
