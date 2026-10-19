using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证历史与实时消息按来源逐条配对，保留合法重复。</summary>
public class DanmuHistoryReconcilerTests
{
    /// <summary>历史匹配已有实时消息的次数，不删除后续合法重复。</summary>
    [Fact]
    public void MatchesOccurrencesAcrossSourcesWithoutRemovingRepeatedLiveMessages()
    {
        var service = new DanmuHistoryReconciler();
        Assert.True(service.AcceptLive(Message()));
        Assert.True(service.AcceptLive(Message()));
        Assert.Empty(service.AcceptHistory([Message(), Message()]));
        Assert.True(service.AcceptLive(Message()));
    }

    /// <summary>历史先到时只逐条抵消相应实时消息。</summary>
    [Fact]
    public void HistoryFirstConsumesOneLiveOccurrenceEach()
    {
        var service = new DanmuHistoryReconciler();
        Assert.Equal(2, service.AcceptHistory([Message(), Message()]).Count);
        Assert.False(service.AcceptLive(Message()));
        Assert.False(service.AcceptLive(Message()));
        Assert.True(service.AcceptLive(Message()));
        Assert.Empty(service.AcceptHistory([Message(), Message(), Message()]));
    }

    /// <summary>没有服务端时间时不根据用户与正文猜测重复。</summary>
    [Fact]
    public void MissingServerTimeDoesNotGuessByUserAndText()
    {
        var service = new DanmuHistoryReconciler();
        var message = Message();
        message.Timestamp = 0;
        Assert.True(service.AcceptLive(message));
        Assert.Single(service.AcceptHistory([message]));
    }

    /// <summary>可靠标识可直接配对，不同房间始终隔离。</summary>
    [Fact]
    public void StableMessageIdMatchesWithoutTimeButDifferentRoomsNeverMatch()
    {
        var service = new DanmuHistoryReconciler();
        var first = Message();
        first.Timestamp = 0;
        first.MessageId = "server-id";
        Assert.True(service.AcceptLive(first));
        Assert.Empty(service.AcceptHistory([first]));
        var other = Message();
        other.RoomId = "200";
        Assert.Single(service.AcceptHistory([other]));
    }

    private static BilibiliDanmuMessage Message()
    {
        return new() { RoomId = "100", Uid = 42, UserName = "用户", Content = "重复", Timestamp = 1800000000 };
    }
}
