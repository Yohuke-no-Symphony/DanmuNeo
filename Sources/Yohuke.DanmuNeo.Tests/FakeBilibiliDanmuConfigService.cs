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
    public DanmuRoomConfig Config { get; set; } = new()
    {
        Colors = [new() { Value = 0xFFFFFF, Name = "白色" }, new() { Value = 0xFF0000, Name = "红色" }],
        Modes = [new() { Value = 1, Name = "滚动" }, new() { Value = 4, Name = "底部" }, new() { Value = 5, Name = "顶部" }]
    };

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

    /// <summary>
    /// 保存使用的房间、账号与选项。
    /// </summary>
    public List<(string RoomId, string Cookie, DanmuSendOptions Options)> SetRequests { get; } = [];

    /// <summary>
    /// 读取使用的超时设置。
    /// </summary>
    public List<TimeSpan?> ReadTimeouts { get; } = [];

    /// <summary>
    /// 保存使用的超时设置。
    /// </summary>
    public List<TimeSpan?> WriteTimeouts { get; } = [];

    /// <summary>
    /// 控制异步读取及其取消行为。
    /// </summary>
    public Func<string, string, CancellationToken, bool, Task<DanmuRoomConfig>>? ReadAsync { get; set; }

    /// <summary>
    /// 控制异步保存及其取消行为。
    /// </summary>
    public Func<string, string, DanmuSendOptions, CancellationToken, Task<DanmuRoomConfig>>? WriteAsync { get; set; }

    /// <inheritdoc/>
    public Task<DanmuRoomConfig> GetConfigAsync(string roomId, string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetRequests.Add((roomId, cookie));
        ReadTimeouts.Add(timeout);

        if (ReadAsync is not null)
        {
            return ReadAsync(roomId, cookie, cancellationToken, forceRefresh);
        }

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
        SetRequests.Add((roomId, cookie, options));
        WriteTimeouts.Add(timeout);

        if (WriteAsync is not null)
        {
            return WriteAsync(roomId, cookie, options, cancellationToken);
        }

        Config = Config with { Current = options };
        return Task.FromResult(Config);
    }
}
