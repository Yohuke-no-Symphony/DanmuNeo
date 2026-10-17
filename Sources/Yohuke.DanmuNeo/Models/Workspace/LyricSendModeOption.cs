using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 歌词发送内容模式选项。
/// </summary>
public class LyricSendModeOption : ObservableObject
{
    private readonly string displayNameKey;

    /// <summary>
    /// 初始化歌词发送内容模式选项。
    /// </summary>
    public LyricSendModeOption(LyricSendMode value, string displayNameKey)
    {
        Value = value;
        this.displayNameKey = displayNameKey;
    }

    /// <summary>
    /// 模式值。
    /// </summary>
    public LyricSendMode Value { get; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName => Localization.Get(displayNameKey);

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayName));
    }
}
