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

    [ObservableProperty] private AppUpdateInfo? availableUpdate;

    [ObservableProperty] private bool isUpdateDialogOpen;

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

            if (!value)
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
        return startupUpdateCheckTask ??= CheckForUpdatesCoreAsync();
    }

    /// <summary>
    /// 关闭本次更新提示。
    /// </summary>
    [RelayCommand]
    public void CloseUpdateDialog()
    {
        IsUpdateDialogOpen = false;
    }

    private async Task CheckForUpdatesCoreAsync()
    {
        if (isDisposed || !CheckForUpdatesOnStartup)
        {
            return;
        }

        using var tokenSource = new CancellationTokenSource();
        updateCheckTokenSource = tokenSource;

        try
        {
            var currentVersion = typeof(MainWindowViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            var update = await appUpdateService.CheckForUpdateAsync(currentVersion, tokenSource.Token);

            if (isDisposed || tokenSource.IsCancellationRequested || update is null)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (isDisposed || tokenSource.IsCancellationRequested || !CheckForUpdatesOnStartup || update is null)
                {
                    return;
                }

                AvailableUpdate = update;
                IsUpdateDialogOpen = true;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StartupLog.Append($"Update check failed: {exception.GetType().Name}");
        }
        finally
        {
            updateCheckTokenSource = null;
        }
    }

    private void DisposeUpdateCheck()
    {
        updateCheckTokenSource?.Cancel();
        ownedAppUpdateService?.Dispose();
        CloseUpdateDialog();
    }
}