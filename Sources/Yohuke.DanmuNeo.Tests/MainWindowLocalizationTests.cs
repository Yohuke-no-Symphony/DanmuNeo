using System.Net;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证主窗口语言刷新保留弹窗草稿、设置分区和快捷键录制状态。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class MainWindowLocalizationTests
{
    /// <summary>
    /// 已打开的新增弹窗和设置选项原位刷新，用户输入及保存文件保持不变。
    /// </summary>
    [AvaloniaFact]
    public void LanguageSwitchRefreshesDialogAndSectionsWithoutSavingOrReplacingDrafts()
    {
        using var language = new LocalizationTestScope();
        using var context = new LocalizationSettingsTestContext("zh-CN");
        var model = context.ViewModel;
        model.OpenAddWorkspaceDialog();
        model.NewWorkspaceName = "用户草稿 User draft";
        var title = model.AddDialogTitle;
        var sections = model.SettingsSectionOptions.ToArray();
        var previousName = sections[0].Name;
        var original = File.ReadAllBytes(context.StateService.SettingsFilePath);

        Localization.SetLanguage("en-US");

        Assert.NotEqual(title, model.AddDialogTitle);
        Assert.NotEqual(previousName, sections[0].Name);
        Assert.Equal("用户草稿 User draft", model.NewWorkspaceName);
        Assert.True(model.IsAddDialogOpen);
        Assert.All(sections, section => Assert.Contains(model.SettingsSectionOptions,
            current => ReferenceEquals(current, section)));
        Assert.Equal(original, File.ReadAllBytes(context.StateService.SettingsFilePath));
    }

    /// <summary>
    /// 快捷键录制过程中改变语言不结束录制、不重建动作行或改变手势。
    /// </summary>
    [AvaloniaFact]
    public void LanguageSwitchKeepsShortcutRecordingAndStableActionRows()
    {
        using var language = new LocalizationTestScope();
        using var context = new LocalizationSettingsTestContext("zh-CN");
        var model = context.ViewModel;
        var groups = model.ShortcutBindingGroups.ToArray();
        var binding = groups[0].Bindings[0];
        var name = binding.Name;
        var gesture = binding.Binding.GestureText;
        model.BeginRecordShortcut(binding);

        Localization.SetLanguage("en-US");

        Assert.True(binding.IsRecording);
        Assert.NotEqual(name, binding.Name);
        Assert.Equal(gesture, binding.Binding.GestureText);
        Assert.Same(binding, groups[0].Bindings[0]);
        Assert.Same(groups[0], model.ShortcutBindingGroups[0]);
        Assert.Contains(binding.Name, model.StatusMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 当前操作提示保留语义键和参数，直接写入的外部提示不会恢复旧的语义状态。
    /// </summary>
    [AvaloniaFact]
    public void StatusUsesSemanticStateAndExternalMessagesReplaceIt()
    {
        using var language = new LocalizationTestScope();
        using var context = new LocalizationSettingsTestContext("zh-CN");
        var model = context.ViewModel;
        model.SetLocalizedStatus("App.Search.Found", 7);
        var before = model.StatusMessage;

        Localization.SetLanguage("en-US");

        Assert.NotEqual(before, model.StatusMessage);
        Assert.Equal(Localization.Format("App.Search.Found", 7), model.StatusMessage);
        model.StatusMessage = "External response 原始数据";
        Localization.SetLanguage("zh-CN");
        Assert.Equal("External response 原始数据", model.StatusMessage);
    }

    /// <summary>
    /// 浏览器登录完成后的当前提示随语言刷新，保存的账号凭据保持不变。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(BrowserLoginPlatform.Bilibili)]
    [InlineData(BrowserLoginPlatform.QQMusic)]
    public async Task SuccessfulBrowserLoginRefreshesItsStatusWithoutChangingCredentials(BrowserLoginPlatform platform)
    {
        using var language = new LocalizationTestScope();
        using var context = new LocalizationSettingsTestContext("zh-CN");
        var result = new BrowserCookieLoginService().CreateResult(platform,
        [
            new Cookie("SESSDATA", "test-session"), new Cookie("bili_jct", "test-csrf"),
            new Cookie("uin", "42"), new Cookie("qm_keyst", "test-key")
        ]);

        if (platform == BrowserLoginPlatform.Bilibili)
        {
            await context.ViewModel.ApplyBilibiliBrowserLoginAsync(result, null);
        }
        else
        {
            await context.ViewModel.ApplyQQMusicBrowserLoginAsync(result);
        }

        var before = context.ViewModel.StatusMessage;
        var originalSettings = File.ReadAllBytes(context.StateService.SettingsFilePath);
        Localization.SetLanguage("en-US");

        Assert.NotEqual(before, context.ViewModel.StatusMessage);
        Assert.Equal(Localization.Get(result.MessageKey!), context.ViewModel.StatusMessage);
        Assert.Equal(originalSettings, File.ReadAllBytes(context.StateService.SettingsFilePath));
        Assert.Equal(result.Cookie, platform == BrowserLoginPlatform.Bilibili
            ? context.ViewModel.Accounts.Last().Cookie
            : context.ViewModel.Settings.QQMusicCookie);
    }
}
