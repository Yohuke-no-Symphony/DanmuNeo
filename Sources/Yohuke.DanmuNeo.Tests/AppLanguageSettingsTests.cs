using System.Text.Json;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证语言设置的兼容默认值、规范化和启动状态复用。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class AppLanguageSettingsTests
{
    /// <summary>
    /// 语言字段缺失时跟随系统，同时保留旧设置中的其他字段。
    /// </summary>
    [Fact]
    public void LegacySettingsWithoutLanguageFollowSystem()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DanmuNeo-language-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var service = new AppStateService(directory);
            File.WriteAllText(service.SettingsFilePath, JsonSerializer.Serialize(new { SendIntervalMs = 1250 }));

            var state = service.Load();

            Assert.Equal("system", state.Settings.Language);
            Assert.Equal(1250, state.Settings.SendIntervalMs);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 修复空白和系统选项，保留尚未安装的语言标识以支持以后恢复。
    /// </summary>
    [Theory]
    [InlineData(null, "system")]
    [InlineData("", "system")]
    [InlineData("  ", "system")]
    [InlineData(" System ", "system")]
    [InlineData("en-US", "en-US")]
    [InlineData(" zh-CN ", "zh-CN")]
    [InlineData("fr-FR", "fr-FR")]
    public void SaveAndLoadNormalizeLanguageWithoutDiscardingUnavailablePacks(string? language, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "DanmuNeo-language-settings-" + Guid.NewGuid().ToString("N"));

        try
        {
            var service = new AppStateService(directory);
            service.Save(new() { Settings = new() { Language = language!, SendIntervalMs = 1250 } });

            var loaded = service.Load();

            Assert.Equal(expected, loaded.Settings.Language);
            Assert.Equal(1250, loaded.Settings.SendIntervalMs);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 主窗口复用启动阶段读取的同一状态，避免构造时重新读取另一份配置。
    /// </summary>
    [Fact]
    public void ViewModelReusesTheStateLoadedBeforeApplyingStartupLanguage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DanmuNeo-language-settings-" + Guid.NewGuid().ToString("N"));

        try
        {
            var service = new AppStateService(directory);
            service.Save(new());
            var state = service.Load();
            state.Settings.Language = "en-US";
            state.Settings.SendIntervalMs = 1250;
            using var viewModel = new MainWindowViewModel(service, new FakeLiveRoomSearchService(),
                new FakeRoomAvatarService(), danmuConfigService: new FakeBilibiliDanmuConfigService(), loadedState: state);

            Assert.Same(state, viewModel.State);
            Assert.Same(state.Settings, viewModel.Settings);
            Assert.Equal(1250, viewModel.Settings.SendIntervalMs);
            Assert.Equal("system", service.Load().Settings.Language);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
