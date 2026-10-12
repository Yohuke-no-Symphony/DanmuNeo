using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证独立紧凑换行偏好的旧配置默认值与设置保存入口。
/// </summary>
public class CompactFeedSettingsTests
{
    /// <summary>
    /// 缺少新字段的旧配置保持普通弹幕单行，同时默认允许 SC 换行。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldSettingsKeepCompatibleWrapDefaults(bool compact)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var service = new AppStateService(directory);
            File.WriteAllText(service.SettingsFilePath,
                $$"""{"CompactDanmuDisplay":{{compact.ToString().ToLowerInvariant()}}}""");

            var state = service.Load();

            Assert.Equal(compact, state.Settings.CompactDanmuDisplay);
            Assert.False(state.Settings.CompactDanmuWrap);
            Assert.True(state.Settings.CompactSuperChatWrap);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 关闭设置页时通过现有保存入口保留四种独立偏好组合，包括宽松模式下的禁用值。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ClosingSettingsSavesIndependentWrapOptions(bool danmuWrap, bool superChatWrap)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var service = new AppStateService(directory);
            using var viewModel = new MainWindowViewModel(service, new FakeLiveRoomSearchService(),
                new FakeRoomAvatarService(), new FakeBilibiliModerationService());
            viewModel.IsCompactDanmuDisplay = true;
            viewModel.IsCompactDanmuWrap = danmuWrap;
            viewModel.IsCompactSuperChatWrap = superChatWrap;
            viewModel.IsCompactDanmuDisplay = false;

            await viewModel.CloseSettingsAsync();
            var loaded = service.Load();

            Assert.False(loaded.Settings.CompactDanmuDisplay);
            Assert.Equal(danmuWrap, loaded.Settings.CompactDanmuWrap);
            Assert.Equal(superChatWrap, loaded.Settings.CompactSuperChatWrap);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
