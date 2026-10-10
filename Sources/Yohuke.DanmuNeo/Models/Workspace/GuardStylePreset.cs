namespace Yohuke.DanmuNeo.Models.Workspace;

internal sealed class GuardStylePreset
{
    /// <summary>
    /// B 站大航海档位：1 为总督，2 为提督，3 为舰长；粉丝牌预览另允许 0 表示普通。
    /// </summary>
    public int GuardLevel { get; init; }

    /// <summary>
    /// 用于档位选择器、粉丝牌预览和上舰记录徽章的档位名称。
    /// </summary>
    public string DisplayName { get; init; } = "";
}
