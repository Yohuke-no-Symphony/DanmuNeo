using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证粉丝牌的短展示名称与完整提示文本。
/// </summary>
public class DanmuFeedItemTests
{
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
    }
}
