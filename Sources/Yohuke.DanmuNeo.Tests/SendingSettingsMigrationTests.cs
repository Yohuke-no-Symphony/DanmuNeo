using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>发送策略与兼容行为的离线回归测试。</summary>
public class SendingSettingsMigrationTests
{
    /// <summary>拆分和旧整包配置迁移旧开关，明确新键优先且写回时删除旧键。</summary>
    [Theory]
    [InlineData("{\"ResendWhenShielded\":false}", false)]
    [InlineData("{\"ResendWhenShielded\":true}", true)]
    [InlineData("{\"RetryWhenRateLimited\":false,\"ResendWhenShielded\":true}", false)]
    [InlineData("{\"ResendWhenShielded\":false,\"RetryWhenRateLimited\":true}", true)]
    [InlineData("{}", true)]
    public void SplitAndLegacySettingsMigrateWithNewKeyPrecedence(string json, bool expected)
    {
        foreach (var legacy in new[] { false, true })
        {
            var directory = Path.Combine(Path.GetTempPath(), "DanmuNeo-send-migration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var service = new AppStateService(directory);
                File.WriteAllText(legacy ? service.StateFilePath : service.SettingsFilePath,
                    legacy ? "{\"Settings\":" + json + "}" : json);
                var state = service.Load();
                Assert.Equal(expected, state.Settings.RetryWhenRateLimited);
                service.Save(state);
                var saved = File.ReadAllText(service.SettingsFilePath);
                Assert.Contains("RetryWhenRateLimited", saved);
                Assert.DoesNotContain("ResendWhenShielded", saved);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>备份恢复路径同样迁移旧的发送重试开关。</summary>
    [Fact]
    public void RecoveredBackupMigratesLegacyRetrySetting()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DanmuNeo-send-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new AppStateService(directory);
            service.Save(new());
            service.Save(new());
            var backup = Directory.GetFiles(service.BackupDirectory, "settings.json*.bak").First();
            File.WriteAllText(backup, "{\"ResendWhenShielded\":false}");
            File.WriteAllText(service.SettingsFilePath, "broken-json");
            Assert.False(service.Load().Settings.RetryWhenRateLimited);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
