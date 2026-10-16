namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 弹幕表情文本与本地图片的展示映射。
/// </summary>
public sealed class DanmuEmoticon
{
    /// <summary>
    /// 正文中的原始表情文本。
    /// </summary>
    public string Text { get; init; } = "";

    /// <summary>
    /// 已缓存的表情图片路径。
    /// </summary>
    public string ImagePath { get; init; } = "";
}
