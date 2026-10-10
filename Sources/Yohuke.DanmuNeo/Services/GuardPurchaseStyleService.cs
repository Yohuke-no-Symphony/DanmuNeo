using Avalonia.Media;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 为上舰记录、粉丝牌和离线预览提供统一的大航海档位颜色。
/// </summary>
public static class GuardPurchaseStyleService
{
    private static readonly Color governorColor = Color.Parse("#E89539");
    private static readonly Color admiralColor = Color.Parse("#B45AB8");
    private static readonly Color captainColor = Color.Parse("#3171D2");

    /// <summary>
    /// 返回总督橙色、提督紫色或舰长蓝色。
    /// </summary>
    public static Color GetBorderColor(int guardLevel)
    {
        return guardLevel switch
        {
            1 => governorColor,
            2 => admiralColor,
            3 => captainColor,
            _ => throw new ArgumentOutOfRangeException(nameof(guardLevel), guardLevel, "无效的大航海档位")
        };
    }
}
