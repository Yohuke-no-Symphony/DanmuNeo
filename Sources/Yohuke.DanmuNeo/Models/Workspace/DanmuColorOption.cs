using System.Globalization;

namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 当前账号在直播间内可用的弹幕颜色。
/// </summary>
public sealed record DanmuColorOption
{
    /// <summary>
    /// 二十四位 RGB 颜色值。
    /// </summary>
    public int Value { get; init; }

    /// <summary>
    /// 服务端提供的颜色名称。
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// 用于界面色块的颜色文本。
    /// </summary>
    public string ColorHex => $"#{Value.ToString("X6", CultureInfo.InvariantCulture)}";
}
