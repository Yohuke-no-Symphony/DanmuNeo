namespace Yohuke.DanmuNeo.Services;

/// <summary>由应用组合根持有的记录模块依赖。</summary>
public sealed class LiveMarkerServices : IDisposable
{
    private readonly IDisposable? ownedClock;

    /// <summary>创建可注入文件、直播取时和本地时钟的记录服务集合。</summary>
    public LiveMarkerServices(ILiveMarkerFileService? files = null, ILiveMarkerClockService? clock = null,
        LiveMarkerExportService? exports = null, TimeProvider? timeProvider = null)
    {
        TimeProvider = timeProvider ?? TimeProvider.System;
        Files = files ?? new LiveMarkerFileService();
        Clock = clock ?? new BilibiliLiveMarkerClockService(timeProvider: TimeProvider);
        Exports = exports ?? new();
        ownedClock = clock is null ? Clock as IDisposable : null;
    }

    /// <summary>文件读写与占用协调。</summary>
    public ILiveMarkerFileService Files { get; }

    /// <summary>新建文件时的一次性 B 站取时。</summary>
    public ILiveMarkerClockService Clock { get; }

    /// <summary>文档导出。</summary>
    public LiveMarkerExportService Exports { get; }

    /// <summary>本地和单调时钟。</summary>
    public TimeProvider TimeProvider { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        ownedClock?.Dispose();
    }
}
