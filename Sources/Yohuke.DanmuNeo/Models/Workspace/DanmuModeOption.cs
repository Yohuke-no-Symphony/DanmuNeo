namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 当前账号在直播间内可用的弹幕位置。
/// </summary>
public sealed record DanmuModeOption
{
    /// <summary>
    /// 服务端定义的位置编号。
    /// </summary>
    public int Value { get; init; }

    /// <summary>
    /// 服务端提供的位置名称。
    /// </summary>
    public string Name { get; init; } = "";
}
