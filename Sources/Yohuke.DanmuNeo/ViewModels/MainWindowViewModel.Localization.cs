using Avalonia.Threading;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.ViewModels;

/// <summary>
/// 主窗口的界面语言选择和显示刷新。
/// </summary>
public partial class MainWindowViewModel
{
    private bool isRefreshingLanguageOptions;
    private Task languageSelectionSaveTask = Task.CompletedTask;
    private readonly Dictionary<string, Func<string>> localizedTextProviders = new(StringComparer.Ordinal);
    private bool isApplyingLocalizedText;

    /// <summary>
    /// 跟随系统及当前已安装的语言选项。
    /// </summary>
    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    /// <summary>
    /// 当前应用的语言选择。
    /// </summary>
    public LanguageOption? SelectedLanguageOption
    {
        get => LanguageOptions.FirstOrDefault(option => !option.IsAvailable &&
                   string.Equals(option.LanguageID, Settings.Language, StringComparison.OrdinalIgnoreCase)) ??
               LanguageOptions.FirstOrDefault(option => option.LanguageID == Localization.SelectedLanguageID);
        set
        {
            if (isDisposed || isRefreshingLanguageOptions || value is null ||
                ReferenceEquals(value, SelectedLanguageOption))
            {
                return;
            }

            _ = SaveLanguageSelectionAsync(value);
        }
    }

    /// <summary>
    /// 应用用户明确选择的语言并保存设置。
    /// </summary>
    public Task SelectLanguageAsync(LanguageOption? option)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (isRefreshingLanguageOptions || option is null || !option.IsAvailable || !LanguageOptions.Contains(option))
        {
            return Task.CompletedTask;
        }

        if (Settings.Language == option.LanguageID && Localization.SelectedLanguageID == option.LanguageID)
        {
            return languageSelectionSaveTask;
        }

        Settings.Language = option.LanguageID;
        Localization.SetLanguage(option.LanguageID);
        OnPropertyChanged(nameof(SelectedLanguageOption));
        languageSelectionSaveTask = SaveAsync();
        return languageSelectionSaveTask;
    }

    /// <summary>
    /// 以语义键和发生时的参数记录当前操作提示，使其随界面语言刷新。
    /// </summary>
    public void SetLocalizedStatus(string key, params object[] arguments)
    {
        SetLocalizedProperty(nameof(StatusMessage), key, arguments);
    }

    private void SetBrowserLoginStatus(BrowserCookieLoginResult result)
    {
        if (result.MessageKey is { } key)
        {
            SetLocalizedStatus(key);
            return;
        }

        StatusMessage = result.Message;
    }

    private void InitializeLocalizedText()
    {
        SetLocalizedProperty(nameof(LiveRoomSearchStatus), "App.Search.Hint");
        SetLocalizedProperty(nameof(DeleteDialogPrimaryText), "App.Dialog.Delete");
        SetLocalizedProperty(nameof(UpdateCheckMessage), "App.Updates.Available");
    }

    private void SetLocalizedProperty(string propertyName, string key, params object[] arguments)
    {
        SetLocalizedProperty(propertyName, () => Localization.Format(key, arguments.Select(argument =>
            argument is Func<string> provider ? provider() : argument).ToArray()));
    }

    private void SetLocalizedProperty(string propertyName, Func<string> textProvider)
    {
        localizedTextProviders[propertyName] = textProvider;
        ApplyLocalizedText(propertyName, textProvider());
    }

    private void ApplyLocalizedText(string propertyName, string text)
    {
        var wasApplying = isApplyingLocalizedText;
        isApplyingLocalizedText = true;

        try
        {
            switch (propertyName)
            {
                case nameof(StatusMessage):
                    StatusMessage = text;
                    break;
                case nameof(LiveRoomSearchStatus):
                    LiveRoomSearchStatus = text;
                    break;
                case nameof(UpdateCheckMessage):
                    UpdateCheckMessage = text;
                    break;
                case nameof(AddDialogTitle):
                    AddDialogTitle = text;
                    break;
                case nameof(AddDialogDescription):
                    AddDialogDescription = text;
                    break;
                case nameof(AddDialogPrimaryText):
                    AddDialogPrimaryText = text;
                    break;
                case nameof(DeleteDialogTitle):
                    DeleteDialogTitle = text;
                    break;
                case nameof(DeleteDialogDescription):
                    DeleteDialogDescription = text;
                    break;
                case nameof(DeleteDialogPrimaryText):
                    DeleteDialogPrimaryText = text;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(propertyName));
            }
        }
        finally
        {
            isApplyingLocalizedText = wasApplying;
        }
    }

    private void ForgetLocalizedText(string propertyName)
    {
        if (!isApplyingLocalizedText)
        {
            localizedTextProviders.Remove(propertyName);
        }
    }

    partial void OnStatusMessageChanged(string value)
    {
        ForgetLocalizedText(nameof(StatusMessage));
    }

    partial void OnLiveRoomSearchStatusChanged(string value)
    {
        ForgetLocalizedText(nameof(LiveRoomSearchStatus));
    }

    partial void OnUpdateCheckMessageChanged(string value)
    {
        ForgetLocalizedText(nameof(UpdateCheckMessage));
    }

    partial void OnAddDialogTitleChanged(string value)
    {
        ForgetLocalizedText(nameof(AddDialogTitle));
    }

    partial void OnAddDialogDescriptionChanged(string value)
    {
        ForgetLocalizedText(nameof(AddDialogDescription));
    }

    partial void OnAddDialogPrimaryTextChanged(string value)
    {
        ForgetLocalizedText(nameof(AddDialogPrimaryText));
    }

    partial void OnDeleteDialogTitleChanged(string value)
    {
        ForgetLocalizedText(nameof(DeleteDialogTitle));
    }

    partial void OnDeleteDialogDescriptionChanged(string value)
    {
        ForgetLocalizedText(nameof(DeleteDialogDescription));
    }

    partial void OnDeleteDialogPrimaryTextChanged(string value)
    {
        ForgetLocalizedText(nameof(DeleteDialogPrimaryText));
    }

    private void RefreshLocalizedMainWindow()
    {
        foreach (var section in SettingsSectionOptions)
        {
            section.RefreshLocalization();
        }

        foreach (var group in ShortcutBindingGroups)
        {
            group.RefreshLocalization();
        }

        foreach (var item in LyricLibrary)
        {
            item.RefreshLocalization();
        }

        foreach (var item in MusicLyricSearchResults)
        {
            item.RefreshLocalization();
        }

        foreach (var result in LiveRoomSearchResults)
        {
            result.RefreshLocalization();
        }

        foreach (var account in Accounts)
        {
            account.RefreshLocalization();
        }

        foreach (var pair in localizedTextProviders.ToArray())
        {
            ApplyLocalizedText(pair.Key, pair.Value());
        }

        OnPropertyChanged(nameof(SidebarToggleToolTip));
        OnPropertyChanged(nameof(AppVersion));
        OnPropertyChanged(nameof(QQMusicCookieStatus));
        OnPropertyChanged(nameof(QQMusicCookieSummary));
        OnPropertyChanged(nameof(SelectedSettingsSection));
    }

    private static IReadOnlyList<LanguageOption> CreateLanguageOptions(string savedLanguageId)
    {
        var options = new List<LanguageOption>
        {
            new("system", string.Empty)
        };
        options.AddRange(Localization.KnownLanguages.Select(language =>
            new LanguageOption(language.LanguageID, language.LanguageName)));

        if (!options.Any(option => string.Equals(option.LanguageID, savedLanguageId, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new(savedLanguageId, string.Empty, false));
        }

        return options;
    }

    private async Task SaveLanguageSelectionAsync(LanguageOption option)
    {
        try
        {
            await SelectLanguageAsync(option);
        }
        catch (Exception exception)
        {
            LogErrorOnce(Localization.Get("Settings.Title"),
                Localization.Get("Settings.Language.SaveFailed"), exception);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        isRefreshingLanguageOptions = true;

        try
        {
            foreach (var option in LanguageOptions)
            {
                option.RefreshLocalization();
            }

            foreach (var option in ThemeModeOptions)
            {
                option.RefreshLocalization();
            }

            RefreshLocalizedMainWindow();
            OnPropertyChanged(nameof(SelectedLanguageOption));
        }
        finally
        {
            isRefreshingLanguageOptions = false;
        }
    }
}
