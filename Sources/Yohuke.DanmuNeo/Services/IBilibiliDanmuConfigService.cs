using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 按直播间和登录身份读取、保存弹幕配置。
/// </summary>
public interface IBilibiliDanmuConfigService
{
    /// <summary>
    /// 获取已确认配置；主动刷新时重新读取服务端。
    /// </summary>
    Task<DanmuRoomConfig> GetConfigAsync(
        string roomId,
        string cookie,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false);

    /// <summary>
    /// 保存可用颜色与位置，并返回新的已确认配置。
    /// </summary>
    Task<DanmuRoomConfig> SetConfigAsync(
        string roomId,
        string cookie,
        DanmuSendOptions options,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
