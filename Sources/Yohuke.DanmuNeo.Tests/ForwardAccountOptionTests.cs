using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 转发账号选项与账号模型的名称通知测试。
/// </summary>
public class ForwardAccountOptionTests
{
    /// <summary>
    /// 账号修改名称会更新同一个选项并发出显示名称通知。
    /// </summary>
    [Fact]
    public void RenamingAccountUpdatesDisplayName()
    {
        var account = new BilibiliAccount { Id = "account", Name = "旧名称" };
        using var option = new ForwardAccountOption(account);
        var changes = new List<string?>();
        option.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        account.Name = "新名称";

        Assert.Equal("account", option.AccountId);
        Assert.Equal("新名称", option.DisplayName);
        Assert.Equal([nameof(ForwardAccountOption.DisplayName)], changes);
    }

    /// <summary>
    /// 释放选项会解除账号通知订阅。
    /// </summary>
    [Fact]
    public void DisposingOptionStopsNotifications()
    {
        var account = new BilibiliAccount { Name = "旧名称" };
        using var option = new ForwardAccountOption(account);
        var notified = false;
        option.PropertyChanged += (_, _) => notified = true;
        option.Dispose();

        account.Name = "新名称";

        Assert.False(notified);
    }
}
