using System.Text.Json;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>首次命名意图与默认账号状态的持久化兼容性。</summary>
public class AccountNameStateTests
{
    /// <summary>旧配置不重新命名；新命名标记的读取不依赖 JSON 属性顺序。</summary>
    [Theory]
    [InlineData("{\"Name\":\"已有账号\"}", false)]
    [InlineData("{\"Name\":\"新账号\",\"AutoNameOnLogin\":true}", true)]
    [InlineData("{\"AutoNameOnLogin\":true,\"Name\":\"新账号\"}", true)]
    public void AutoNamingIsBackwardCompatibleAndOrderIndependent(string json, bool pending)
    {
        var account = JsonSerializer.Deserialize<BilibiliAccount>(json)!;
        Assert.Equal(pending, account.AutoNameOnLogin);
        Assert.Equal(pending, JsonSerializer.Deserialize<BilibiliAccount>(JsonSerializer.Serialize(account))!.AutoNameOnLogin);
    }

    /// <summary>多个旧默认只保留第一个，重新加载保持同一默认。</summary>
    [Fact]
    public void NormalizationKeepsExactlyOneDefaultAccount()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var service = new AppStateService(directory);
            service.Save(new()
            {
                Accounts =
                [
                    new() { Id = "first", IsGlobalDefault = true },
                    new() { Id = "second", IsGlobalDefault = true, AutoNameOnLogin = true }
                ]
            });
            var loaded = service.Load();
            Assert.Equal("first", Assert.Single(loaded.Accounts, account => account.IsGlobalDefault).Id);
            Assert.True(loaded.Accounts[1].AutoNameOnLogin);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
