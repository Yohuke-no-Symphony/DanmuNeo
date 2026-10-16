using Yohuke.DanmuNeo.Models.Updates;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用临时配置和离线更新响应的主窗口上下文。
/// </summary>
public sealed class AppUpdateTestContext : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 以默认设置或指定的旧配置 JSON 创建上下文。
    /// </summary>
    public AppUpdateTestContext(string? settingsJson = null)
    {
        StateService = new(directory);
        Directory.CreateDirectory(directory);

        if (settingsJson is not null)
        {
            File.WriteAllText(StateService.SettingsFilePath, settingsJson);
        }

        ViewModel = new(StateService, avatarCacheService: new FakeRoomAvatarService(),
            appUpdateService: UpdateService);
    }

    public AppStateService StateService { get; }
    public FakeAppUpdateService UpdateService { get; } = new();
    public MainWindowViewModel ViewModel { get; }

    public AppUpdateInfo Update { get; } = new(new(9, 8, 7, 0), "v9.8.7",
        new("https://github.com/Yohuke-no-Symphony/DanmuNeo/releases/tag/v9.8.7"));

    /// <inheritdoc/>
    public void Dispose()
    {
        ViewModel.Dispose();
        Directory.Delete(directory, true);
    }
}