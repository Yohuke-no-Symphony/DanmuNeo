using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证实际设置界面的实时语言绑定、选择保存和业务状态隔离。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class LocalizationUiTests
{
    /// <summary>
    /// 用户在语言下拉框中的选择立即生效并保存，选项和主题对象保持不变。
    /// </summary>
    [AvaloniaFact]
    public async Task LanguageSelectorAppliesAndPersistsSelectionWithoutReplacingOptions()
    {
        using var language = new LocalizationTestScope();
        using var context = new LocalizationSettingsTestContext("zh-CN");
        var model = context.ViewModel;
        var options = model.LanguageOptions.ToArray();
        var themes = model.ThemeModeOptions.ToArray();
        var view = new GlobalSettingsView { DataContext = model };
        var window = new Window { Width = 800, Height = 900, Content = view };

        try
        {
            window.Show();
            var selector = view.FindControl<ComboBox>("InterfaceLanguageChoice")!;
            var english = options.Single(option => option.LanguageID == "en-US");
            selector.SelectedItem = english;
            Assert.Equal("en-US", model.Settings.Language);
            await model.SelectLanguageAsync(english);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("en-US", Localization.SelectedLanguageID);
            Assert.Equal("en-US", model.Settings.Language);
            Assert.Equal("en-US", context.StateService.Load().Settings.Language);
            Assert.Same(english, selector.SelectedItem);
            Assert.Same(model, view.DataContext);
            Assert.All(options, option => Assert.Contains(model.LanguageOptions, current => ReferenceEquals(current, option)));
            Assert.All(themes, option => Assert.Contains(model.ThemeModeOptions, current => ReferenceEquals(current, option)));
            Assert.Equal(Localization.Get("Settings.Language.Label"),
                view.FindControl<TextBlock>("InterfaceLanguageLabel")!.Text);

            Localization.SetLanguage("zh-CN");
            AppLanguageService.ApplyPreference(context.StateService.Load().Settings.Language);

            Assert.Equal("en-US", Localization.CurrentLanguageID);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 已打开视图即时更新文本，保留无效数值草稿且不自动保存用户配置。
    /// </summary>
    [AvaloniaFact]
    public void DirectLanguageSwitchPreservesInvalidDraftAndDoesNotSaveSettings()
    {
        using var language = new LocalizationTestScope();
        using var context = new LocalizationSettingsTestContext("system");
        Localization.SetLanguage("zh-CN");
        var model = context.ViewModel;
        var view = new GlobalSettingsView { DataContext = model };
        var window = new Window { Width = 800, Height = 900, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var originalSettings = File.ReadAllBytes(context.StateService.SettingsFilePath);
            var label = view.FindControl<TextBlock>("InterfaceLanguageLabel")!;
            var oldLabel = label.Text;
            var themeLabel = view.FindControl<TextBlock>("ThemeModeLabel")!;
            var oldThemeLabel = themeLabel.Text;
            var theme = view.FindControl<ComboBox>("ThemeModeChoice")!;
            var selectedTheme = model.SelectedThemeModeOption;
            Assert.Same(selectedTheme, theme.SelectedItem);
            var input = view.FindControl<TextBox>("TimeoutSecondsInput")!;
            var timeout = model.Settings.TimeoutSeconds;
            input.Text = "unfinished-number";
            Dispatcher.UIThread.RunJobs();
            Assert.True(DataValidationErrors.GetHasErrors(input));

            Localization.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(oldLabel, label.Text);
            Assert.Equal(Localization.Get("Settings.Language.Label"), label.Text);
            Assert.NotEqual(oldThemeLabel, themeLabel.Text);
            Assert.Equal(Localization.Get("Settings.Theme.Label"), themeLabel.Text);
            Assert.Same(selectedTheme, theme.SelectedItem);
            Assert.Equal(Localization.Get("Settings.Language.System"), model.LanguageOptions[0].LanguageName);
            Assert.Equal(Localization.Get("Settings.Theme.Light"),
                model.ThemeModeOptions.Single(option => option.Value == AppThemeMode.Light).Name);
            Assert.Equal("unfinished-number", input.Text);
            Assert.Equal(timeout, model.Settings.TimeoutSeconds);
            Assert.Equal("system", model.Settings.Language);
            Assert.Equal(originalSettings, File.ReadAllBytes(context.StateService.SettingsFilePath));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 已卸载语言包只影响显示回退，初始化下拉框和保存主题均保留原语言偏好。
    /// </summary>
    [AvaloniaFact]
    public async Task MissingLanguagePackFallsBackWithoutOverwritingSavedPreference()
    {
        using var language = new LocalizationTestScope();
        using var context = new LocalizationSettingsTestContext("fr-FR");
        var model = context.ViewModel;
        var originalSettings = File.ReadAllBytes(context.StateService.SettingsFilePath);
        var originalTheme = Application.Current!.RequestedThemeVariant;
        var view = new GlobalSettingsView { DataContext = model };
        var window = new Window { Width = 800, Height = 900, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("en-US", Localization.CurrentLanguageID);
            Assert.Equal("fr-FR", model.SelectedLanguageOption!.LanguageID);
            Assert.Equal("fr-FR", model.Settings.Language);
            Assert.Equal(originalSettings, File.ReadAllBytes(context.StateService.SettingsFilePath));

            await model.SelectThemeModeAsync(model.ThemeModeOptions.Single(option => option.Value == AppThemeMode.Dark));

            Assert.Equal("en-US", Localization.CurrentLanguageID);
            Assert.Equal("fr-FR", context.StateService.Load().Settings.Language);
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = originalTheme;
        }
    }

    /// <summary>
    /// 缺失语言的占位项允许用户从下拉框明确改用英文并替换原保存值。
    /// </summary>
    [AvaloniaFact]
    public async Task MissingSavedLanguageCanBeReplacedWithEnglishFromTheSelector()
    {
        using var language = new LocalizationTestScope();
        using var context = new LocalizationSettingsTestContext("fr-FR");
        var model = context.ViewModel;
        var view = new GlobalSettingsView { DataContext = model };
        var window = new Window { Width = 800, Height = 900, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var selector = view.FindControl<ComboBox>("InterfaceLanguageChoice")!;
            Assert.Equal("fr-FR", Assert.IsType<LanguageOption>(selector.SelectedItem).LanguageID);
            var english = model.LanguageOptions.Single(option => option.LanguageID == "en-US");

            selector.SelectedItem = english;

            Assert.Equal("en-US", model.Settings.Language);
            await model.SelectLanguageAsync(english);
            Assert.Equal("en-US", context.StateService.Load().Settings.Language);
            Assert.Same(english, selector.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 后台回调读取统一语言快照，切换界面语言不会修改数值文化。
    /// </summary>
    [AvaloniaFact]
    public async Task BackgroundTextUsesSelectedLanguageAndKeepsNumericCulture()
    {
        using var language = new LocalizationTestScope();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        Localization.SetLanguage("en-US");
        var expected = Localization.Get("Settings.Language.Label");

        var text = await Task.Run(() =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            return Localization.Get("Settings.Language.Label");
        });

        Assert.Equal(expected, text);
        Assert.Equal("fr-FR", CultureInfo.CurrentCulture.Name);
        Assert.Equal("1,5", 1.5m.ToString(CultureInfo.CurrentCulture));
    }

    /// <summary>
    /// 重复选择和未知语言不产生语言变化通知，也不改变有效选择。
    /// </summary>
    [AvaloniaFact]
    public void UnchangedOrInvalidSelectionLeavesLanguageStateUntouched()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var notifications = 0;
        void OnChanged(object? sender, EventArgs e)
        {
            notifications++;
        }

        Localization.LanguageChanged += OnChanged;

        try
        {
            Localization.SetLanguage("ZH-cn");
            Assert.Throws<ArgumentException>(() => Localization.SetLanguage("uninstalled"));

            Assert.Equal(0, notifications);
            Assert.Equal("zh-CN", Localization.SelectedLanguageID);
            Assert.Equal("zh-CN", Localization.CurrentLanguageID);
        }
        finally
        {
            Localization.LanguageChanged -= OnChanged;
        }
    }

    /// <summary>
    /// 文本订阅释放后不再接收静态语言服务的更新。
    /// </summary>
    [AvaloniaFact]
    public void DisposedTextSubscriptionStopsReceivingLanguageChanges()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var observer = new LocalizationTextObserver();
        using var subscription = Localization.Observe("Settings.Language.Label").Subscribe(observer);
        Assert.Equal(Localization.Get("Settings.Language.Label"), Assert.Single(observer.Values));
        subscription.Dispose();

        Localization.SetLanguage("en-US");

        Assert.NotEqual(Localization.Get("Settings.Language.Label"), Assert.Single(observer.Values));
    }

    /// <summary>
    /// 主窗口视图模型释放后解除静态事件，旧选项不会跨界面生命周期继续刷新。
    /// </summary>
    [AvaloniaFact]
    public void DisposedViewModelStopsRefreshingLanguageOptions()
    {
        using var language = new LocalizationTestScope();
        using var context = new LocalizationSettingsTestContext("system");
        var notifications = 0;
        context.ViewModel.LanguageOptions[0].PropertyChanged += (_, _) => notifications++;
        context.ViewModel.Dispose();

        Localization.SetLanguage(Localization.SelectedLanguageID == "en-US" ? "zh-CN" : "en-US");

        Assert.Equal(0, notifications);
    }
}
