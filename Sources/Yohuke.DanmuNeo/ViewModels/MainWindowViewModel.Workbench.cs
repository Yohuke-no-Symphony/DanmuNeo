namespace Yohuke.DanmuNeo.ViewModels;

/// <summary>
/// 工作台的平台菜单偏好。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>
    /// 是否在 macOS 上使用主窗口内菜单。
    /// </summary>
    public bool UseWindowMenuOnMac
    {
        get => Settings.UseWindowMenuOnMac;
        set
        {
            if (Settings.UseWindowMenuOnMac == value)
            {
                return;
            }

            Settings.UseWindowMenuOnMac = value;
            OnPropertyChanged();
            _ = SaveMenuPreferenceAsync();
        }
    }

    /// <summary>
    /// 当前平台是否支持切换 macOS 菜单模式。
    /// </summary>
    public bool IsMacOS => OperatingSystem.IsMacOS();

    private async Task SaveMenuPreferenceAsync()
    {
        try
        {
            await SaveAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"菜单设置保存失败：{exception.Message}";
            LogErrorOnce("菜单设置", StatusMessage, exception);
        }
    }
}
