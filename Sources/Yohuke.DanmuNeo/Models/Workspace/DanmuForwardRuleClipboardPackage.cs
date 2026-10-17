namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 可复制和粘贴的转发规则配置包。
/// </summary>
public sealed class DanmuForwardRuleClipboardPackage
{
    /// <summary>
    /// 转发规则剪贴板格式标识。
    /// </summary>
    public const string FORMAT = "Yohuke.DanmuNeo.ForwardRules";

    /// <summary>
    /// 当前支持的剪贴板格式版本。
    /// </summary>
    public const int CURRENT_VERSION = 1;

    /// <summary>
    /// 配置包的格式标识。
    /// </summary>
    public string Format { get; set; } = FORMAT;

    /// <summary>
    /// 配置包的格式版本。
    /// </summary>
    public int Version { get; set; } = CURRENT_VERSION;

    /// <summary>
    /// 按复制顺序排列的规则配置。
    /// </summary>
    public List<DanmuForwardRuleClipboardEntry> Rules { get; set; } = [];
}
