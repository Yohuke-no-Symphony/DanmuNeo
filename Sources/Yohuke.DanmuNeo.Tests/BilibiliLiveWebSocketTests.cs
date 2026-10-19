using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;

namespace Yohuke.DanmuNeo.Tests;

public class BilibiliLiveWebSocketTests
{
    [Fact]
    public void ParseDanmuMessageKeepsServerTimestampAndIdentity()
    {
        var json = JObject.Parse("""
            {"cmd":"DANMU_MSG","msg_id":"server-id","info":[[0,0,0,0,1800000000123],"内容",[42,"用户"]]}
            """);
        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", json);
        Assert.NotNull(message);
        Assert.Equal(1800000000, message.Timestamp);
        Assert.Equal("server-id", message.MessageId);
    }

    [Fact]
    public void ParseDanmuMessageReadsUserAndContent()
    {
        var json = JObject.Parse("""
        {
          "cmd": "DANMU_MSG",
          "info": [
            [],
            "你好",
            [42, "用户A"]
          ]
        }
        """);

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", json);

        Assert.NotNull(message);
        Assert.Equal("100", message.RoomId);
        Assert.Equal(42, message.Uid);
        Assert.Equal("用户A", message.UserName);
        Assert.Equal("你好", message.Content);
        Assert.False(message.IsEmoticon);
    }

    [Fact]
    public void ParseDanmuMessageMarksProtocolEmoticonWithSingleBracketContent()
    {
        var json = JObject.Parse("""
        {
          "cmd": "DANMU_MSG",
          "info": [
            [
              { "emoticon_unique": "dog" }
            ],
            "[dog]",
            [42, "用户A"]
          ]
        }
        """);

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", json);

        Assert.NotNull(message);
        Assert.True(message.IsEmoticon);
    }

    [Theory]
    [InlineData("[a][b]")]
    [InlineData("文字[a]")]
    [InlineData("[]")]
    [InlineData("[a]\n")]
    public void ParseDanmuMessageDoesNotMarkNonSingleEmoticonContent(string content)
    {
        var serializedContent = System.Text.Json.JsonSerializer.Serialize(content);
        var json = JObject.Parse($$"""
        {
          "cmd": "DANMU_MSG",
          "info": [
            [
              { "emoticon_unique": "dog" }
            ],
            {{serializedContent}},
            [42, "用户A"]
          ]
        }
        """);

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", json);

        Assert.NotNull(message);
        Assert.False(message.IsEmoticon);
    }

    [Fact]
    public void ParseDanmuMessageDoesNotMarkBracketTextWithoutProtocolMetadata()
    {
        var json = JObject.Parse("""
        {
          "cmd": "DANMU_MSG",
          "info": [
            [],
            "[公告]",
            [42, "用户A"]
          ]
        }
        """);

        var message = BilibiliLiveWebSocket.ParseDanmuMessage("100", json);

        Assert.NotNull(message);
        Assert.False(message.IsEmoticon);
    }

    [Fact]
    public void CreateWebSocketUriUsesWssPort()
    {
        var uri = BilibiliLiveWebSocket.CreateWebSocketUri(new BilibiliDanmuHost
        {
            Host = "zj-cn-live-comet.chat.bilibili.com",
            WssPort = 2245
        });

        Assert.Equal("wss://zj-cn-live-comet.chat.bilibili.com:2245/sub", uri.ToString());
    }

    [Theory]
    [InlineData("DANMU_MSG", "DANMU_MSG", true)]
    [InlineData("DANMU_MSG:4:0:2:2:2:0", "DANMU_MSG", true)]
    [InlineData("SUPER_CHAT_MESSAGE_JPN", "SUPER_CHAT_MESSAGE", true)]
    [InlineData("SEND_GIFT", "DANMU_MSG", false)]
    public void IsCommandMatchesBilibiliCommandPrefix(string command, string expectedCommand, bool expected)
    {
        Assert.Equal(expected, BilibiliLiveWebSocket.IsCommand(command, expectedCommand));
    }

    [Fact]
    public void ParseSuperChatMessageReadsPriceAndContent()
    {
        var json = JObject.Parse("""
        {
          "cmd": "SUPER_CHAT_MESSAGE",
          "data": {
            "id": 987654321,
            "message": "SC内容",
            "price": 30,
            "price_text": "￥30",
            "background_color_start": "#3171D2",
            "ts": 123456,
            "user_info": {
              "uname": "用户B"
            }
          }
        }
        """);

        var message = BilibiliLiveWebSocket.ParseSuperChatMessage("200", json);

        Assert.NotNull(message);
        Assert.Equal("987654321", message.MessageId);
        Assert.Equal("200", message.RoomId);
        Assert.Equal("用户B", message.UserName);
        Assert.Equal(30, message.Price);
        Assert.Equal("￥30", message.PriceText);
        Assert.Equal("#3171D2", message.BorderColor);
        Assert.Equal("SC内容", message.Content);
    }

    [Fact]
    public void ParseSuperChatMessageUsesNextValidBilibiliColor()
    {
        var json = JObject.Parse("""
        {
          "cmd": "SUPER_CHAT_MESSAGE",
          "data": {
            "message": "SC内容",
            "price": 50,
            "background_color_start": "invalid",
            "background_bottom_color": "#2A60B2",
            "background_price_color": "#7497CD",
            "ts": 123456,
            "user_info": {
              "uname": "用户B"
            }
          }
        }
        """);

        var message = BilibiliLiveWebSocket.ParseSuperChatMessage("200", json);

        Assert.NotNull(message);
        Assert.Equal("￥50", message.PriceText);
        Assert.Equal("#2A60B2", message.BorderColor);
    }

    [Fact]
    public void ParseSuperChatMessageFallsBackToDefaultColor()
    {
        var json = JObject.Parse("""
        {
          "cmd": "SUPER_CHAT_MESSAGE",
          "data": {
            "message": "SC内容",
            "price": 30,
            "background_color_start": "invalid",
            "ts": 123456,
            "user_info": {
              "uname": "用户B"
            }
          }
        }
        """);

        var message = BilibiliLiveWebSocket.ParseSuperChatMessage("200", json);

        Assert.NotNull(message);
        Assert.Equal("#2A60B2", message.BorderColor);
    }

    [Fact]
    public void ParseSuperChatMessageUsesPriceColorAfterInvalidPrimaryColors()
    {
        var json = JObject.Parse("""
        {
          "cmd": "SUPER_CHAT_MESSAGE",
          "data": {
            "message": "SC内容",
            "price": 100,
            "background_color_start": "invalid",
            "background_bottom_color": "also-invalid",
            "background_price_color": "#E2B52B",
            "ts": 123456,
            "user_info": {
              "uname": "用户B"
            }
          }
        }
        """);

        var message = BilibiliLiveWebSocket.ParseSuperChatMessage("200", json);

        Assert.NotNull(message);
        Assert.Equal("#E2B52B", message.BorderColor);
    }
}
