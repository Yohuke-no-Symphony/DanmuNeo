using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证设置行显示在线身份，同时保留用户标签与原凭据。
/// </summary>
public class AccountSettingsValidationTests
{
    /// <summary>
    /// 构造不联网，手动刷新只更新运行时资料。
    /// </summary>
    [AvaloniaFact]
    public async Task RefreshShowsRealIdentityWithoutRenamingAccountOrSerializingStatus()
    {
        var validation = new ControlledAccountValidationService();
        using var sessions = new BilibiliAccountSessionService(validation);
        var account = new BilibiliAccount { Name = "工作账号", Cookie = "SESSDATA=offline;bili_jct=csrf" };
        sessions.Register(account);
        using var row = new AccountSettingsItemViewModel(account, sessions, () => TimeSpan.FromSeconds(3));
        Assert.Empty(validation.Requests);
        var refreshing = row.RefreshAsync();
        validation.Requests.Single().SetResult(new()
        {
            Status = AccountValidationStatus.Valid, Uid = 42, UserName = "真实用户", CheckedAt = DateTimeOffset.Now
        });
        await refreshing;

        Assert.Equal(Localization.Get("App.Account.Validation.Valid"), row.StatusText);
        Assert.Equal("真实用户 · UID 42", row.ProfileText);
        Assert.Equal("工作账号", account.Name);
        var json = System.Text.Json.JsonSerializer.Serialize(account);
        Assert.DoesNotContain("真实用户", json);
        Assert.DoesNotContain("CheckedAt", json);
    }

    /// <summary>网络暂时不可验证时即使缓存着用户身份，也不能继续亮绿灯。</summary>
    [AvaloniaFact]
    public async Task UnavailableValidationDoesNotKeepGreenLightFromCachedProfile()
    {
        var validation = new ControlledAccountValidationService();
        using var sessions = new BilibiliAccountSessionService(validation);
        var account = new BilibiliAccount { Cookie = "SESSDATA=offline;bili_jct=offline" };
        sessions.Register(account);
        using var row = new AccountSettingsItemViewModel(account, sessions, () => TimeSpan.FromSeconds(3));
        var first = row.RefreshAsync();
        validation.Requests[0].SetResult(new() { Status = AccountValidationStatus.Valid, Uid = 42, UserName = "有效用户" });
        await first;
        Assert.True(row.IsValid);
        var second = row.RefreshAsync();
        Assert.True(row.IsUncertain);
        Assert.False(row.IsValid);
        validation.Requests[1].SetResult(new() { Status = AccountValidationStatus.Unavailable });
        await second;
        Assert.Equal("有效用户 · UID 42", row.ProfileText);
        Assert.True(row.IsUncertain);
        Assert.False(row.IsValid);
        Assert.False(row.IsInvalid);
    }
}
