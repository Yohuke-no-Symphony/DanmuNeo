using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证普通弹幕的房管身份、现代粉丝牌优先级及可选元数据缺失时的兼容性。
/// </summary>
public class BilibiliDanmuMetadataParserTests
{
    /// <summary>
    /// 传统协议使用佩戴牌子的档位，不使用当前房间的身份。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LegacyMedalGuardTierIsIndependentOfCurrentRoom(int level)
    {
        var medal = new JArray(25, "星光", "主播", 200, 0, "", 0, 0, 0, 0, level, 1, 20);
        var json = new JObject
        {
            ["info"] = new JArray(new JArray(), "弹幕", new JArray(42, "用户", 0), medal,
                new JArray(), new JArray(), 0, level == 0 ? 1 : 0)
        };

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", json);

        Assert.NotNull(message);
        Assert.Equal(level, message.MedalGuardLevel);
    }

    /// <summary>
    /// 现代粉丝牌档位优先于传统字段，包括有效的普通档位零。
    /// </summary>
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    [InlineData("\"3\"", 3)]
    [InlineData("null", 0)]
    [InlineData("{}", 0)]
    [InlineData("-1", 0)]
    [InlineData("4", 0)]
    public void ModernMedalGuardTierOverridesLegacyAndIgnoresUnknownValues(string guardToken, int expected)
    {
        var metadata = new JArray(Enumerable.Repeat<JToken>(JValue.CreateNull(), 16));
        metadata[15] = new JObject
        {
            ["user"] = new JObject
            {
                ["uid"] = 42,
                ["medal"] = new JObject { ["name"] = "新牌", ["level"] = 25, ["guard_level"] = JToken.Parse(guardToken) },
                ["guard"] = new JObject { ["level"] = 1 }
            }
        };
        var legacyMedal = new JArray(25, "旧牌", "主播", 200, 0, "", 0, 0, 0, 0, 2, 1, 20);
        var json = new JObject
        {
            ["info"] = new JArray(metadata, "弹幕", new JArray(42, "用户", 0), legacyMedal,
                new JArray(), new JArray(), 0, 3)
        };

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", json);

        Assert.NotNull(message);
        Assert.Equal("新牌", message.MedalName);
        Assert.Equal(expected, message.MedalGuardLevel);
    }

    /// <summary>
    /// 验证旧数组协议保留房管标记，并区分粉丝牌等级与直播用户等级。
    /// </summary>
    [Fact]
    public void LegacyDanmuReadsAdminMedalAndUserLevelSeparately()
    {
        var json = JObject.Parse("""
                                 {
                                   "info": [[], "你好", [42, "用户A", 1], [23, "粉丝牌"], [40]]
                                 }
                                 """);

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", json);

        Assert.NotNull(message);
        Assert.True(message.IsRoomAdmin);
        Assert.Equal("粉丝牌", message.MedalName);
        Assert.Equal(23, message.MedalLevel);
        Assert.Equal(40, message.UserLevel);
    }

    /// <summary>
    /// 验证现代用户资料和佩戴粉丝牌覆盖旧数组中不同的用户与牌子信息。
    /// </summary>
    [Fact]
    public void ModernUserAndMedalOverrideLegacyValues()
    {
        var metadata = new JArray(Enumerable.Repeat<JToken>(JValue.CreateNull(), 16));
        metadata[15] = JObject.Parse("""
                                     {
                                       "user": {
                                         "uid": 43,
                                         "base": { "name": "现代用户" },
                                         "medal": { "name": "新牌子", "level": 37 }
                                       }
                                     }
                                     """);
        var json = new JObject
        {
            ["info"] = new JArray(metadata, "你好", new JArray(42, "旧用户", 0), new JArray(23, "旧牌子"))
        };

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", json);

        Assert.NotNull(message);
        Assert.Equal(43, message.Uid);
        Assert.Equal("现代用户", message.UserName);
        Assert.Equal("新牌子", message.MedalName);
        Assert.Equal(37, message.MedalLevel);
        Assert.False(message.IsRoomAdmin);
    }

    /// <summary>
    /// 验证空数组、短数组、无牌和 null 元数据均保留普通弹幕且不伪造等级。
    /// </summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("[[], \"你好\", [42, \"用户A\"]]")]
    [InlineData("[[], \"你好\", [42, \"用户A\", false], []]")]
    [InlineData("[[], \"你好\", [42, \"用户A\"], null, null]")]
    public void MissingOptionalMetadataKeepsOrdinaryDanmu(string info)
    {
        var json = new JObject { ["info"] = JArray.Parse(info) };

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", json);

        Assert.NotNull(message);
        Assert.False(message.IsRoomAdmin);
        Assert.Empty(message.MedalName);
        Assert.Equal(0, message.MedalLevel);
        Assert.Equal(0, message.MedalGuardLevel);
        Assert.Equal(0, message.UserLevel);
    }

    /// <summary>
    /// 验证错误类型的房管标记和非数字等级不会中断普通弹幕内容解析。
    /// </summary>
    [Fact]
    public void MalformedOptionalValuesDoNotInterruptDanmuParsing()
    {
        var json = JObject.Parse("""
                                 {
                                   "info": [[], "你好", [42, "用户A", {}], ["invalid", "粉丝牌"], ["invalid"]]
                                 }
                                 """);

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", json);

        Assert.NotNull(message);
        Assert.Equal("你好", message.Content);
        Assert.Equal(0, message.MedalLevel);
        Assert.Equal(0, message.UserLevel);
        Assert.False(message.IsRoomAdmin);
    }
}
