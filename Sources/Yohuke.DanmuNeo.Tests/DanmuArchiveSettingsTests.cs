using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证旧配置默认启用留档且保存后保留显式关闭。</summary>
public class DanmuArchiveSettingsTests
{
    /// <summary>旧配置默认启用，显式关闭经过保存与归一化后仍然有效。</summary>
    [Fact]
    public void OldSettingsEnableArchivesAndExplicitFalseSurvivesSaveAndNormalize()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{}");
            var service = new AppStateService(directory);
            var state = service.Load();
            Assert.True(state.Settings.ArchiveReceivedDanmu);
            state.Settings.ArchiveReceivedDanmu = false;
            service.Save(state);
            Assert.False(service.Load().Settings.ArchiveReceivedDanmu);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
