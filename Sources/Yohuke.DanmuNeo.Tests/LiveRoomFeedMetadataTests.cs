using Avalonia.Media;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 房间弹幕元数据与付费事件列表测试。
/// </summary>
public class LiveRoomFeedMetadataTests
{
    /// <summary>
    /// 收到的粉丝牌名称与等级映射到展示项。
    /// </summary>
    [Fact]
    public void DanmuPreservesMedalAndAdminStatus()
    {
        using var context = new RoomModerationTestContext();
        context.Room.AddDanmu(new()
        {
            RoomId = "100", Uid = 42, UserName = "带牌用户", Content = "弹幕", IsRoomAdmin = true,
            MedalName = "夜更", MedalLevel = 25
        });

        var item = context.Room.DanmuItems[0];
        Assert.Equal("100", item.RoomId);
        Assert.True(item.IsRoomAdmin);
        Assert.True(item.HasMedal);
        Assert.Equal("夜更 25", item.MedalText);
        Assert.False(context.Item.HasMedal);
        Assert.Empty(context.Item.MedalText);
    }

    /// <summary>
    /// 三个上舰档位和 SC 保持各自展示语义，并且上舰不能插入输入框。
    /// </summary>
    [Theory]
    [InlineData(1, "总督", "#E89539")]
    [InlineData(2, "提督", "#B45AB8")]
    [InlineData(3, "舰长", "#3171D2")]
    public void GuardAndSuperChatShareListWithoutMixingMeaning(int level, string label, string color)
    {
        using var context = new RoomModerationTestContext();
        context.Room.AddSuperChat(new()
        {
            MessageId = "sc1", RoomId = "100", UserName = "SC用户", Content = "留言", PriceText = "¥30",
            Timestamp = 1800000000
        });
        context.Room.AddGuardPurchase(new()
        {
            RoomId = "100", Uid = 42, UserName = "上舰用户", GuardLevel = level, Timestamp = 1800000001, Quantity = 1
        });

        Assert.Equal(2, context.Room.SuperChats.Count);
        var purchase = context.Room.SuperChats[0];
        Assert.Equal(LivePaidEventKind.GuardPurchase, purchase.Kind);
        Assert.Equal(label, purchase.BadgeText);
        Assert.Equal(Color.Parse(color), purchase.BorderColor);
        Assert.Equal("上舰用户", purchase.UserName);
        Assert.Empty(purchase.PriceText);
        Assert.Empty(purchase.Content);
        Assert.False(purchase.CanInsertContent);
        Assert.Equal("¥30", context.Room.SuperChats[1].BadgeText);
        context.Room.CopySuperChat(purchase);
        Assert.Empty(context.Room.InputDraft);
        context.Room.CopySuperChat(context.Room.SuperChats[1]);
        Assert.Contains("留言", context.Room.InputDraft);
    }

    /// <summary>
    /// 相同购买记录去重，后续续费保留。
    /// </summary>
    [Fact]
    public void GuardDuplicateIsSuppressedButRenewalRemains()
    {
        using var context = new RoomModerationTestContext();
        var purchase = new BilibiliGuardPurchaseMessage
        {
            RoomId = "100", Uid = 42, UserName = "用户", GuardLevel = 3, Timestamp = 1800000000, Quantity = 1
        };
        context.Room.AddGuardPurchase(purchase);
        context.Room.AddGuardPurchase(purchase);
        Assert.Single(context.Room.SuperChats);
        purchase.Timestamp++;
        context.Room.AddGuardPurchase(purchase);
        Assert.Equal(2, context.Room.SuperChats.Count);
    }

    /// <summary>
    /// 没有服务器时间时显示接收时间，不使用虚构时间作去重。
    /// </summary>
    [Fact]
    public void GuardWithoutTimestampUsesReceptionTimeOnlyForDisplay()
    {
        using var context = new RoomModerationTestContext();
        var before = DateTimeOffset.Now;
        context.Room.AddGuardPurchase(new() { RoomId = "100", Uid = 42, UserName = "用户", GuardLevel = 3 });

        Assert.InRange(context.Room.SuperChats.Single().Time, before, DateTimeOffset.Now);
    }
}
