namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 服务端确认的弹幕发送颜色与位置。
/// </summary>
public sealed record DanmuSendOptions
{
    /// <summary>
    /// 二十四位 RGB 颜色值。
    /// </summary>
    public int Color { get; init; } = 0xFFFFFF;

    /// <summary>
    /// 服务端定义的弹幕位置编号。
    /// </summary>
    public int Mode { get; init; } = 1;

    /// <summary>
    /// 是否符合颜色和位置编号的取值范围。
    /// </summary>
    public bool IsValid => Color is >= 0 and <= 0xFFFFFF && Mode > 0;
}
