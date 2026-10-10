using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证普通弹幕的房管身份、现代粉丝牌优先级及可选元数据缺失时的兼容性。
/// </summary>
public class BilibiliDanmuMetadataParserTests
{
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
