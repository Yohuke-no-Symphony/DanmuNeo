using System.Text;
using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证大航海购买消息的协议兼容、服务器身份保留与独立事件分发。
/// </summary>
public class BilibiliGuardPurchaseParserTests
{
    /// <summary>
    /// 验证旧购买协议保留用户、数量和服务器时间，并正确映射三个大航海档位。
    /// </summary>
    [Theory]
    [InlineData(1, "总督")]
    [InlineData(2, "提督")]
    [InlineData(3, "舰长")]
    public void LegacyPurchaseReadsUserTierAndServerTimestamp(int guardLevel, string name)
    {
        var json = CreateLegacyPurchase(guardLevel);

        var message = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", json);

        Assert.NotNull(message);
        Assert.Equal("100", message.RoomId);
        Assert.Equal(42, message.Uid);
        Assert.Equal("用户A", message.UserName);
        Assert.Equal(guardLevel, message.GuardLevel);
        Assert.Equal(name, message.GuardLevelName);
        Assert.Equal(1800000000, message.Timestamp);
        Assert.Equal(2, message.Quantity);
        Assert.Empty(message.MessageId);
        Assert.False(message.IsGroup);
    }

    /// <summary>
    /// 验证现代赠送和团体记录分别读取购买者、接收者、支付数量及真实消息 ID。
    /// </summary>
    [Fact]
    public void ModernPurchaseReadsStructuredUserAndPaymentFields()
    {
        var json = CreateModernPurchase();
        json["data"]!["option"]!["is_group"] = true;
        json["data"]!["option"]!["source"] = 1;
        json["data"]!["msg_id"] = "server-message-1";

        var message = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", json);

        Assert.NotNull(message);
        Assert.Equal("用户A", message.UserName);
        Assert.Equal(42, message.Uid);
        Assert.Equal(3, message.GuardLevel);
        Assert.Equal(1800000000, message.Timestamp);
        Assert.Equal(2, message.Quantity);
        Assert.Equal(99, message.ReceiverUid);
        Assert.True(message.IsGroup);
        Assert.Equal("server-message-1", message.MessageId);
    }

    /// <summary>
    /// 验证同一笔购买的旧协议和现代协议生成一致且可重复计算的去重身份。
    /// </summary>
    [Fact]
    public void LegacyAndModernVariantsShareStableIdentity()
    {
        var legacy = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", CreateLegacyPurchase(3));
        var modern = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", CreateModernPurchase());

        Assert.NotNull(legacy);
        Assert.NotNull(modern);
        Assert.NotEmpty(legacy.Identity);
        Assert.Equal(legacy.Identity, modern.Identity);
        Assert.Equal(modern.Identity,
            BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", CreateModernPurchase())!.Identity);
    }

    /// <summary>
    /// 验证相邻服务器时刻的连续购买不会因用户和档位相同而合并。
    /// </summary>
    [Fact]
    public void ConsecutivePurchasesKeepDistinctServerTimes()
    {
        var first = CreateLegacyPurchase(3);
        var next = CreateLegacyPurchase(3);
        next["data"]!["start_time"] = 1800000001;

        var firstMessage = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", first);
        var nextMessage = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", next);

        Assert.NotNull(firstMessage);
        Assert.NotNull(nextMessage);
        Assert.NotEqual(firstMessage.Identity, nextMessage.Identity);
    }

    /// <summary>
    /// 验证同一秒相同用户、档位和数量的不同真实消息 ID 不会被字段回退键合并。
    /// </summary>
    [Fact]
    public void DistinctServerIdsKeepSameSecondPurchasesSeparate()
    {
        var first = CreateModernPurchase();
        first["data"]!["msg_id"] = "purchase-1";
        var next = CreateModernPurchase();
        next["data"]!["msg_id"] = "purchase-2";

        var firstMessage = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", first);
        var nextMessage = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", next);

        Assert.NotNull(firstMessage);
        Assert.NotNull(nextMessage);
        Assert.Equal(firstMessage.Timestamp, nextMessage.Timestamp);
        Assert.NotEqual(firstMessage.Identity, nextMessage.Identity);
    }

    /// <summary>
    /// 验证服务器 ID 相同的协议副本保留同一身份，即使时间元数据不同。
    /// </summary>
    [Fact]
    public void RepeatedServerIdKeepsIdentityAcrossProtocolAndTimestampDifferences()
    {
        var legacy = CreateLegacyPurchase(3);
        legacy["data"]!["id"] = "purchase-1";
        var modern = CreateModernPurchase();
        modern["data"]!["msg_id"] = "purchase-1";
        modern["data"]!["guard_info"]!["start_time"] = 1800000001;

        var legacyMessage = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", legacy);
        var modernMessage = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", modern);

        Assert.NotNull(legacyMessage);
        Assert.NotNull(modernMessage);
        Assert.NotEqual(legacyMessage.Timestamp, modernMessage.Timestamp);
        Assert.Equal(legacyMessage.Identity, modernMessage.Identity);
    }

    /// <summary>
    /// 验证旧协议和现代协议均拒绝非大航海用户、负值及未知档位。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void UnknownGuardLevelsAreIgnored(int guardLevel)
    {
        Assert.Null(BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", CreateLegacyPurchase(guardLevel)));
        var modern = CreateModernPurchase();
        modern["data"]!["guard_info"]!["guard_level"] = guardLevel;
        Assert.Null(BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", modern));
    }

    /// <summary>
    /// 验证现代协议的 source=2 提示副本不会产生第二条购买记录。
    /// </summary>
    [Fact]
    public void ModernDuplicateSourceIsIgnored()
    {
        var json = CreateModernPurchase();
        json["data"]!["option"]!["source"] = 2;

        Assert.Null(BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", json));
    }

    /// <summary>
    /// 验证较早的提示协议仍接收续费记录，并保留档位和服务器时间。
    /// </summary>
    [Fact]
    public void EarlierToastProtocolReadsPurchaseAndRenewal()
    {
        var json = CreateLegacyPurchase(2);
        json["cmd"] = "USER_TOAST_MSG";
        json["data"]!["op_type"] = 2;

        var message = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", json);

        Assert.NotNull(message);
        Assert.Equal("提督", message.GuardLevelName);
        Assert.Equal(1800000000, message.Timestamp);
    }

    /// <summary>
    /// 验证缺少服务器时间和 ID 时保留未知状态，不生成客户端伪身份。
    /// </summary>
    [Fact]
    public void MissingTimestampDoesNotInventTimestampOrId()
    {
        var json = CreateLegacyPurchase(3);
        ((JObject)json["data"]!).Remove("start_time");

        var message = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", json);

        Assert.NotNull(message);
        Assert.Equal(0, message.Timestamp);
        Assert.Empty(message.MessageId);
        Assert.Empty(message.Identity);
    }

    /// <summary>
    /// 验证缺少服务器时间时可使用真实数值消息 ID 生成稳定去重身份。
    /// </summary>
    [Fact]
    public void RealServerIdProvidesIdentityWhenTimestampIsMissing()
    {
        var json = CreateLegacyPurchase(3);
        ((JObject)json["data"]!).Remove("start_time");
        json["data"]!["id"] = 12345;

        var message = BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", json);

        Assert.NotNull(message);
        Assert.Equal("12345", message.MessageId);
        Assert.NotEmpty(message.Identity);
    }

    /// <summary>
    /// 验证缺失或错误形状的数据、进房提示及无效用户 UID 不会生成购买记录。
    /// </summary>
    [Fact]
    public void MalformedPayloadAndUnrelatedCommandsAreIgnored()
    {
        Assert.Null(BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", JObject.Parse("""{"cmd":"GUARD_BUY"}""")));
        Assert.Null(
            BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", JObject.Parse("""{"cmd":"GUARD_BUY","data":[]}""")));
        var json = CreateLegacyPurchase(3);
        json["cmd"] = "WELCOME_GUARD";
        Assert.Null(BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", json));
        json["cmd"] = "GUARD_BUY";
        json["data"]!["uid"] = "invalid";
        Assert.Null(BilibiliLiveWebSocket.ParseGuardPurchaseMessage("100", json));
    }

    /// <summary>
    /// 验证三种购买协议只分发大航海事件，不混入普通弹幕或 SC 事件。
    /// </summary>
    [Theory]
    [InlineData("GUARD_BUY")]
    [InlineData("USER_TOAST_MSG")]
    [InlineData("USER_TOAST_MSG_V2")]
    public void BusinessDispatchPublishesGuardPurchaseWithoutPublishingOtherFeeds(string command)
    {
        var json = command == "USER_TOAST_MSG_V2" ? CreateModernPurchase() : CreateLegacyPurchase(3);
        json["cmd"] = command;
        using var socket = new BilibiliLiveWebSocket(100, "");
        var purchases = 0;
        var danmu = 0;
        var superChats = 0;
        socket.GuardPurchaseReceived += (_, message) =>
        {
            purchases++;
            Assert.Equal(42, message.Uid);
            Assert.Equal("舰长", message.GuardLevelName);
        };
        socket.DanmuReceived += (_, _) => danmu++;
        socket.SuperChatReceived += (_, _) => superChats++;

        socket.AnalyseBusinessPackage(Encoding.UTF8.GetBytes(json.ToString()));

        Assert.Equal(1, purchases);
        Assert.Equal(0, danmu);
        Assert.Equal(0, superChats);
    }

    private static JObject CreateLegacyPurchase(int guardLevel)
    {
        return new()
        {
            ["cmd"] = "GUARD_BUY",
            ["data"] = new JObject
            {
                ["uid"] = 42,
                ["username"] = "用户A",
                ["guard_level"] = guardLevel,
                ["num"] = 2,
                ["start_time"] = 1800000000
            }
        };
    }

    private static JObject CreateModernPurchase()
    {
        return JObject.Parse("""
                             {
                               "cmd": "USER_TOAST_MSG_V2",
                               "data": {
                                 "sender_uinfo": { "uid": 42, "base": { "name": "用户A" } },
                                 "receiver_uinfo": { "uid": 99 },
                                 "guard_info": { "guard_level": 3, "start_time": 1800000000 },
                                 "pay_info": { "num": 2 },
                                 "option": { "source": 0, "is_group": false },
                                 "toast_msg": "<%用户A%> 开通了舰长"
                               }
                             }
                             """);
    }
}
