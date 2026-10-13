namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 指定登录身份在直播间内的弹幕配置快照。
/// </summary>
public sealed record DanmuRoomConfig
{
    /// <summary>
    /// 已解锁的弹幕颜色。
    /// </summary>
    public IReadOnlyList<DanmuColorOption> Colors { get; init; } = [];

    /// <summary>
    /// 已解锁的弹幕位置。
    /// </summary>
    public IReadOnlyList<DanmuModeOption> Modes { get; init; } = [];

    /// <summary>
    /// 服务端确认的当前颜色与位置。
    /// </summary>
    public DanmuSendOptions Current { get; init; } = new();
}
