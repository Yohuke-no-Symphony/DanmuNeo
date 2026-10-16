namespace Yohuke.DanmuNeo.Apis.Models.Bilibili;

/// <summary>
/// B 站直播弹幕中的表情映射。
/// </summary>
public class BilibiliDanmuEmoticon
{
    /// <summary>
    /// 弹幕正文中对应的表情文本。
    /// </summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// 表情图片地址。
    /// </summary>
    public string Url { get; set; } = "";
}
