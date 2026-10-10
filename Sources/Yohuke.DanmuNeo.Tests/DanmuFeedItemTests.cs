using Yohuke.DanmuNeo.Models.Workspace;
using Avalonia.Media;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证粉丝牌的短展示名称与完整提示文本。
/// </summary>
public class DanmuFeedItemTests
{
    /// <summary>
    /// 普通和未知档位保持无色边缘，三个大航海档位使用与上舰记录一致的颜色。
    /// </summary>
    [Theory]
    [InlineData(0, "Transparent")]
    [InlineData(1, "#E89539")]
    [InlineData(2, "#B45AB8")]
    [InlineData(3, "#3171D2")]
    [InlineData(4, "Transparent")]
    [InlineData(-1, "Transparent")]
    public void MedalBorderUsesOnlyRecognizedGuardTiers(int guardLevel, string color)
    {
        var item = new DanmuFeedItem { MedalName = "星光", MedalLevel = 25, MedalGuardLevel = guardLevel };

        Assert.Equal(Color.Parse(color), item.MedalBorderColor);
        Assert.Equal(item.MedalBorderColor, Assert.IsAssignableFrom<ISolidColorBrush>(item.MedalBorderBrush).Color);
    }

    /// <summary>
    /// 缺少有效粉丝牌时，大航海档位不能产生徽章边缘色。
    /// </summary>
    [Theory]
    [InlineData("", 25)]
    [InlineData("星光", 0)]
    public void MissingMedalHasNoGuardBorder(string name, int level)
    {
        var item = new DanmuFeedItem { MedalName = name, MedalLevel = level, MedalGuardLevel = 3 };

        Assert.Equal(Colors.Transparent, item.MedalBorderColor);
    }

    /// <summary>
    /// 展示牌名最多三个完整文字元素，并保留等级与原始名称。
    /// </summary>
    [Theory]
    [InlineData("星光", "星光 25")]
    [InlineData("吸夕猫", "吸夕猫 25")]
    [InlineData("四字牌名", "四字牌 25")]
    [InlineData("星🌙光后", "星🌙光 25")]
    public void MedalDisplayLimitsNameWithoutChangingTooltip(string name, string expected)
    {
        var item = new DanmuFeedItem { MedalName = name, MedalLevel = 25 };

        Assert.Equal(expected, item.MedalDisplayText);
        Assert.Equal(expected[..^3], item.MedalDisplayName);
        Assert.Equal($"{name} 25", item.MedalText);
        Assert.Equal(name, item.MedalName);
    }

    /// <summary>
    /// 无有效粉丝牌时不生成展示文本。
    /// </summary>
    [Theory]
    [InlineData("", 25)]
    [InlineData("星光", 0)]
    public void MissingMedalHasNoDisplayText(string name, int level)
    {
        var item = new DanmuFeedItem { MedalName = name, MedalLevel = level };

        Assert.Empty(item.MedalDisplayText);
        Assert.Empty(item.MedalDisplayName);
    }
}
