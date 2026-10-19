namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 指定登录身份在直播间内的弹幕配置快照。
/// </summary>
public sealed record DanmuRoomConfig
{
    /// <summary>当前账号在目标房间可发送的最大 UTF-16 预算；0 表示未知。</summary>
    public int Length { get; init; }

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
