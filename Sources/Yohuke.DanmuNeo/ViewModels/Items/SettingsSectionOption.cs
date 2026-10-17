using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>
/// 设置页分区选项。
/// </summary>
public partial class SettingsSectionOption : ObservableObject
{
    private readonly string nameKey;
    private readonly string descriptionKey;

    /// <summary>
    /// 初始化设置页分区选项。
    /// </summary>
    public SettingsSectionOption(string key, string name, string description)
    {
        Key = key;
        nameKey = name;
        descriptionKey = description;
    }

    /// <summary>
    /// 分区键。
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name => Localization.Get(nameKey);

    /// <summary>
    /// 描述。
    /// </summary>
    public string Description => Localization.Get(descriptionKey);

    /// <summary>
    /// 是否为当前选择。
    /// </summary>
    [ObservableProperty]
    private bool isSelected;

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
    }
}
