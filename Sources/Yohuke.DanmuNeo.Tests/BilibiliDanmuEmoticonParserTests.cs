using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证原项目使用的混排表情与整条表情包协议。
/// </summary>
public class BilibiliDanmuEmoticonParserTests
{
    /// <summary>
    /// 同时兼容字符串和对象形式的 extra，并按字典键保留重复表情的映射。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedEmoticonsUseProtocolKeysAndKeepOriginalContent(bool serialized)
    {
        const string CONTENT = "文字[a.b]+[dog][a.b]+[无效]";
        var extra = JObject.Parse("""
                                {
                                  "emots": {
                                    "[a.b]+": { "text": "显示名称", "url": "http://i0.hdslb.com/one.png" },
                                    "[dog]": { "url": "//i0.hdslb.com/two.webp" },
                                    "[未使用]": { "url": "https://i0.hdslb.com/unused.png" },
                                    "[无效]": { "url": "file:///tmp/not-an-emoticon.png" }
                                  }
                                }
                                """);
        var metadata = CreateMetadata();
        metadata[15] = new JObject { ["extra"] = serialized ? extra.ToString(Formatting.None) : extra };

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", CreateMessage(metadata, CONTENT));

        Assert.NotNull(message);
        Assert.Equal(CONTENT, message.Content);
        Assert.False(message.IsEmoticon);
        Assert.Equal(new[] { "[a.b]+", "[dog]" }, message.Emoticons.Select(emoticon => emoticon.Text));
        Assert.Equal(new[] { "https://i0.hdslb.com/one.png", "https://i0.hdslb.com/two.webp" },
            message.Emoticons.Select(emoticon => emoticon.Url));
    }

    /// <summary>
    /// 旧项目的表情标志与图片字段即可显示整条表情包，无需额外 unique 字段。
    /// </summary>
    [Fact]
    public void StandaloneStickerUsesLegacyFlagAndImage()
    {
        var metadata = CreateMetadata();
        metadata[12] = 1;
        metadata[13] = new JObject { ["url"] = "https://i0.hdslb.com/sticker.gif" };

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", CreateMessage(metadata, "[喵]"));

        Assert.NotNull(message);
        Assert.True(message.IsEmoticon);
        var emoticon = Assert.Single(message.Emoticons);
        Assert.Equal("[喵]", emoticon.Text);
        Assert.Equal("https://i0.hdslb.com/sticker.gif", emoticon.Url);
    }

    /// <summary>
    /// extra 中的单独表情可以作为旧字段缺失时的图片来源。
    /// </summary>
    [Fact]
    public void StandaloneStickerCanUseExtraImage()
    {
        var metadata = CreateMetadata();
        metadata[12] = 1;
        metadata[15] = new JObject
        {
            ["extra"] = new JObject
            {
                ["emoticon"] = new JObject { ["url"] = "https://i0.hdslb.com/sticker.png" }
            }.ToString(Formatting.None)
        };

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", CreateMessage(metadata, "[表情包]"));

        Assert.NotNull(message);
        Assert.Equal("https://i0.hdslb.com/sticker.png", Assert.Single(message.Emoticons).Url);
    }

    /// <summary>
    /// 无效的可选 JSON 不影响弹幕原文。
    /// </summary>
    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("")]
    public void MalformedExtraFallsBackToText(string extra)
    {
        var metadata = CreateMetadata();
        metadata[15] = new JObject { ["extra"] = extra };

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", CreateMessage(metadata, "原文[公告]"));

        Assert.NotNull(message);
        Assert.Equal("原文[公告]", message.Content);
        Assert.Empty(message.Emoticons);
    }

    private static JArray CreateMetadata()
    {
        return new(Enumerable.Repeat<JToken>(JValue.CreateNull(), 16));
    }

    private static JObject CreateMessage(JArray metadata, string content)
    {
        return new() { ["info"] = new JArray(metadata, content, new JArray(42, "表情用户", 0)) };
    }
}
