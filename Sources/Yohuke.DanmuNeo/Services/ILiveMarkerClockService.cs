namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 为新的直播记录文件同步直播时长参考起点。
/// </summary>
public interface ILiveMarkerClockService
{
    /// <summary>
    /// 根据 B 站直播时长反算本机墙钟对应的文件开始时间。
    /// </summary>
    Task<DateTimeOffset> CreateStartTimeAsync(string roomId, string? cookie, TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
