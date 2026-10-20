using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 在一次打开期间用单调时间推进直播时长，并独立提供当前真实时间。
/// </summary>
public sealed class LiveMarkerClock
{
    private readonly TimeProvider timeProvider;
    private TimeSpan initialElapsed;
    private long startedTimestamp;
    private bool isStarted;

    /// <summary>
    /// 初始化独立于网络的文件运行时钟。
    /// </summary>
    public LiveMarkerClock(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 取得本次打开期间的直播时长；起点超前时保留负值。
    /// </summary>
    public TimeSpan Elapsed
    {
        get
        {
            if (!isStarted)
            {
                throw LocalizedExceptionText.Create("Services.LiveMarkerClock.NotStarted",
                    message => new InvalidOperationException(message));
            }

            return initialElapsed + timeProvider.GetElapsedTime(startedTimestamp);
        }
    }

    /// <summary>
    /// 当前本地真实时间，每次读取直接取墙钟，不受文件起点影响。
    /// </summary>
    public DateTimeOffset LocalNow => timeProvider.GetLocalNow();

    /// <summary>
    /// 从文件的持久化起点重新建立本次打开的单调参考。
    /// </summary>
    public void Start(DateTimeOffset startTime)
    {
        initialElapsed = LocalNow - startTime;
        startedTimestamp = timeProvider.GetTimestamp();
        isStarted = true;
    }
}
