using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证文件打开后的直播时间与逐条捕获的真实时间互不联动。
/// </summary>
public class LiveMarkerClockTests
{
    /// <summary>
    /// 文件起点初始化时长，打开后继续按单调时间推进。
    /// </summary>
    [Fact]
    public void StartInitializesFromFileTimeAndAdvancesWithoutDayWrap()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new LiveMarkerTestTimeProvider(now);
        var clock = new LiveMarkerClock(timeProvider);

        clock.Start(now.AddHours(-25));
        timeProvider.Advance(TimeSpan.FromMilliseconds(2250));

        Assert.Equal(TimeSpan.FromHours(25).Add(TimeSpan.FromMilliseconds(2250)), clock.Elapsed);
        Assert.Equal(now.AddMilliseconds(2250), clock.LocalNow);
    }

    /// <summary>
    /// 系统墙钟前跳或后跳只影响真实时间，不改变本次打开期间的直播内时间。
    /// </summary>
    [Theory]
    [InlineData(-3)]
    [InlineData(7)]
    public void WallClockChangeDoesNotAffectElapsed(int jumpHours)
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new LiveMarkerTestTimeProvider(now);
        var clock = new LiveMarkerClock(timeProvider);
        clock.Start(now.AddHours(-1));
        timeProvider.Advance(TimeSpan.FromSeconds(3));

        timeProvider.SetUtcNow(timeProvider.GetUtcNow().AddHours(jumpHours));
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(5)), clock.Elapsed);
        Assert.Equal(now.AddHours(jumpHours).AddSeconds(5), clock.LocalNow);
    }

    /// <summary>
    /// 超前的文件起点保持负时长，直到单调时钟自然追上。
    /// </summary>
    [Fact]
    public void FutureStartPreservesNegativeElapsed()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new LiveMarkerTestTimeProvider(now);
        var clock = new LiveMarkerClock(timeProvider);

        clock.Start(now.AddSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(-30), clock.Elapsed);
        timeProvider.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal(TimeSpan.FromSeconds(-15), clock.Elapsed);
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(5), clock.Elapsed);
    }

    /// <summary>
    /// 重新打开另一个文件时重新建立参考，不复用前一个文件的时钟起点。
    /// </summary>
    [Fact]
    public void StartingAnotherFileReplacesTheClockReference()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new LiveMarkerTestTimeProvider(now);
        var clock = new LiveMarkerClock(timeProvider);
        clock.Start(now.AddHours(-2));
        timeProvider.Advance(TimeSpan.FromMinutes(3));

        clock.Start(timeProvider.GetLocalNow().AddMinutes(-5));

        Assert.Equal(TimeSpan.FromMinutes(5), clock.Elapsed);
    }

    /// <summary>
    /// 真实时间每次读取都保留当前本地时区，不使用文件起点的时区。
    /// </summary>
    [Fact]
    public void LocalNowUsesTheCurrentLocalTimeZone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("LiveMarkerTest", TimeSpan.FromHours(8), "LiveMarkerTest", "LiveMarkerTest");
        var timeProvider = new LiveMarkerTestTimeProvider(new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), zone);
        var clock = new LiveMarkerClock(timeProvider);

        Assert.Equal(TimeSpan.FromHours(8), clock.LocalNow.Offset);
        Assert.Equal(20, clock.LocalNow.Hour);
    }

    /// <summary>
    /// 未打开文件时不能读取没有参考起点的直播内时间。
    /// </summary>
    [Fact]
    public void ElapsedRequiresStart()
    {
        var clock = new LiveMarkerClock();

        Assert.Throws<InvalidOperationException>(() => clock.Elapsed);
    }
}
