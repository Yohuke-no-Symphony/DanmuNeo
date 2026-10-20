namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 提供能够分别推进墙钟与单调时间的离线测试时钟。
/// </summary>
public sealed class LiveMarkerTestTimeProvider : TimeProvider
{
    private DateTimeOffset utcNow;
    private long elapsedTicks;

    /// <summary>
    /// 初始化 UTC 和本地时区。
    /// </summary>
    public LiveMarkerTestTimeProvider(DateTimeOffset utcNow, TimeZoneInfo? localTimeZone = null)
    {
        this.utcNow = utcNow.ToUniversalTime();
        LocalTimeZone = localTimeZone ?? TimeZoneInfo.Utc;
    }

    /// <inheritdoc/>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc/>
    public override TimeZoneInfo LocalTimeZone { get; }

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }

    /// <inheritdoc/>
    public override long GetTimestamp()
    {
        return elapsedTicks;
    }

    /// <summary>
    /// 同时推进墙钟和单调时间。
    /// </summary>
    public void Advance(TimeSpan elapsed)
    {
        utcNow = utcNow.Add(elapsed);
        elapsedTicks += elapsed.Ticks;
    }

    /// <summary>
    /// 模拟系统校时，不改变单调时间。
    /// </summary>
    public void SetUtcNow(DateTimeOffset value)
    {
        utcNow = value.ToUniversalTime();
    }
}
