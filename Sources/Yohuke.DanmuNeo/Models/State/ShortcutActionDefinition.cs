using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Models.State;

/// <summary>
/// 快捷键动作定义。
/// </summary>
public class ShortcutActionDefinition
{
    private readonly string groupNameKey;
    private readonly string nameKey;

    /// <summary>
    /// 初始化快捷键动作定义。
    /// </summary>
    public ShortcutActionDefinition(
        string actionKey,
        string groupKey,
        string groupName,
        string name,
        string defaultGestureText)
    {
        ActionKey = actionKey;
        GroupKey = groupKey;
        groupNameKey = groupName;
        nameKey = name;
        DefaultGestureText = defaultGestureText;
    }

    /// <summary>
    /// 动作键。
    /// </summary>
    public string ActionKey { get; }

    /// <summary>
    /// 分组键。
    /// </summary>
    public string GroupKey { get; }

    /// <summary>
    /// 分组名称。
    /// </summary>
    public string GroupName => Localization.Get(groupNameKey);

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name => Localization.Get(nameKey);

    /// <summary>
    /// 默认快捷键文本。
    /// </summary>
    public string DefaultGestureText { get; }
}
