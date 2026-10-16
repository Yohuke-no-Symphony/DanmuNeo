using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.Updates;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels;

public partial class MainWindowViewModel
{
    private readonly IAppUpdateService appUpdateService;
    private readonly GitHubAppUpdateService? ownedAppUpdateService;
    private CancellationTokenSource? updateCheckTokenSource;
    private Task? startupUpdateCheckTask;
    private Task? updateCheckTask;
    private bool isManualUpdateCheck;

    [ObservableProperty] private AppUpdateInfo? availableUpdate;

    [ObservableProperty] private bool isUpdateDialogOpen;
    [ObservableProperty] private bool isCheckingForUpdates;
    [ObservableProperty] private string updateCheckMessage = "发现新版本";

    /// <summary>
    /// 是否在启动时自动检查应用更新。
    /// </summary>
    public bool CheckForUpdatesOnStartup
    {
        get => Settings.CheckForUpdatesOnStartup;
        set
        {
            if (Settings.CheckForUpdatesOnStartup == value)
            {
                return;
            }

            Settings.CheckForUpdatesOnStartup = value;
            OnPropertyChanged();

            if (!value && !isManualUpdateCheck)
            {
                updateCheckTokenSource?.Cancel();
                CloseUpdateDialog();
            }
        }
    }

    /// <summary>
    /// 在应用启动后执行一次后台更新检查。
    /// </summary>
    public Task CheckForUpdatesOnStartupAsync()
    {
        return startupUpdateCheckTask ??= isDisposed || !CheckForUpdatesOnStartup
            ? Task.CompletedTask
            : StartUpdateCheck(false);
    }

    /// <summary>
    /// 手动检查更新并显示检查中、无更新和失败状态。
    /// </summary>
    public Task CheckForUpdatesManuallyAsync()
    {
        if (isDisposed)
        {
            return Task.CompletedTask;
        }

        isManualUpdateCheck = true;
        IsUpdateDialogOpen = true;
        return StartUpdateCheck(true);
    }

    /// <summary>
    /// 关闭本次更新提示。
    /// </summary>
    [RelayCommand]
    public void CloseUpdateDialog()
    {
        if (isManualUpdateCheck && IsCheckingForUpdates)
        {
            updateCheckTokenSource?.Cancel();
        }

        IsUpdateDialogOpen = false;
    }

    private Task StartUpdateCheck(bool manual)
    {
        if (updateCheckTask is { IsCompleted: false } && updateCheckTokenSource is { IsCancellationRequested: false })
        {
            isManualUpdateCheck |= manual;
            return updateCheckTask;
        }

        var tokenSource = new CancellationTokenSource();
        updateCheckTokenSource = tokenSource;
        isManualUpdateCheck = manual;
        AvailableUpdate = null;
        IsCheckingForUpdates = true;
        UpdateCheckMessage = "正在检查更新…";
        return updateCheckTask = CheckForUpdatesCoreAsync(tokenSource);
    }

    private async Task CheckForUpdatesCoreAsync(CancellationTokenSource tokenSource)
    {
        try
        {
            var currentVersion = typeof(MainWindowViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            var update = await appUpdateService.CheckForUpdateAsync(currentVersion, tokenSource.Token);

            if (!OwnsUpdateCheck(tokenSource))
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!OwnsUpdateCheck(tokenSource))
                {
                    return;
                }

                AvailableUpdate = update;
                UpdateCheckMessage = update is null ? "当前已是最新版本" : "发现新版本";
                IsUpdateDialogOpen = isManualUpdateCheck || update is not null && CheckForUpdatesOnStartup;
            });
        }
        catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StartupLog.Append($"Update check failed: {exception.GetType().Name}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (OwnsUpdateCheck(tokenSource) && isManualUpdateCheck)
                {
                    UpdateCheckMessage = "检查更新失败，请检查网络后重试。";
                    IsUpdateDialogOpen = true;
                }
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(updateCheckTokenSource, tokenSource))
                {
                    updateCheckTokenSource = null;
                    IsCheckingForUpdates = false;
                }
            });
            tokenSource.Dispose();
        }
    }

    private bool OwnsUpdateCheck(CancellationTokenSource tokenSource)
    {
        return !isDisposed && !tokenSource.IsCancellationRequested &&
               ReferenceEquals(updateCheckTokenSource, tokenSource);
    }

    private void DisposeUpdateCheck()
    {
        updateCheckTokenSource?.Cancel();
        ownedAppUpdateService?.Dispose();
        CloseUpdateDialog();
    }
}
