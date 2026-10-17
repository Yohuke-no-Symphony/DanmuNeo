using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>
/// 描述语言选择中的稳定标识和显示名称。
/// </summary>
public sealed class LanguageOption : ObservableObject
{
    private readonly string languageName;

    /// <summary>
    /// 创建已安装语言或跟随系统的选择项。
    /// </summary>
    public LanguageOption(string languageId, string languageName, bool isAvailable = true)
    {
        LanguageID = languageId;
        this.languageName = languageName;
        IsAvailable = isAvailable;
    }

    /// <summary>
    /// 语言包标识或 system。
    /// </summary>
    public string LanguageID { get; }

    /// <summary>
    /// 语言的本地名称或当前界面语言下的跟随系统标签。
    /// </summary>
    public string LanguageName
    {
        get
        {
            if (!IsAvailable)
            {
                return Localization.Format("Settings.Language.Unavailable", LanguageID);
            }

            return LanguageID == "system" ? Localization.Get("Settings.Language.System") : languageName;
        }
    }

    /// <summary>
    /// 对应语言包当前是否可用于选择。
    /// </summary>
    public bool IsAvailable { get; }

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(LanguageName));
    }
}
