using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证粉丝牌自动设置仅存于账号且旧状态默认关闭。</summary>
public class AccountMedalStateTests
{
    /// <summary>旧账号未配置自动换牌时保持关闭，启用后通过拆分账号存储往返。</summary>
    [Fact]
    public void OldAccountsDefaultToDisabledAndPreferenceRoundTrips()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new AppStateService(directory);
            File.WriteAllText(service.AccountsFilePath, """[{"Id":"account","Name":"离线账号"}]""");
            var state = service.Load();
            Assert.False(Assert.Single(state.Accounts).AutoSwitchMedal);
            state.Accounts[0].AutoSwitchMedal = true;
            service.Save(state);
            var restored = service.Load();
            Assert.True(Assert.Single(restored.Accounts).AutoSwitchMedal);
            Assert.DoesNotContain("AutoSwitchMedal", File.ReadAllText(service.WorkspacesFilePath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
