using System.Threading;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 为发送与界面测试提供可控制的弹幕配置。
/// </summary>
public class FakeBilibiliDanmuConfigService : IBilibiliDanmuConfigService
{
    /// <summary>
    /// 默认返回的配置快照。
    /// </summary>
    public DanmuRoomConfig Config { get; set; } = new();

    /// <summary>
    /// 按房间和 Cookie 返回测试配置。
    /// </summary>
    public Func<string, string, DanmuRoomConfig>? ResolveConfig { get; set; }

    /// <summary>
    /// 查询时模拟的错误。
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// 查询使用的房间与账号身份。
    /// </summary>
    public List<(string RoomId, string Cookie)> GetRequests { get; } = [];

    /// <inheritdoc/>
    public Task<DanmuRoomConfig> GetConfigAsync(string roomId, string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetRequests.Add((roomId, cookie));

        if (Exception is not null)
        {
            return Task.FromException<DanmuRoomConfig>(Exception);
        }

        return Task.FromResult(ResolveConfig?.Invoke(roomId, cookie) ?? Config);
    }

    /// <inheritdoc/>
    public Task<DanmuRoomConfig> SetConfigAsync(string roomId, string cookie, DanmuSendOptions options,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Config = Config with { Current = options };
        return Task.FromResult(Config);
    }
}
