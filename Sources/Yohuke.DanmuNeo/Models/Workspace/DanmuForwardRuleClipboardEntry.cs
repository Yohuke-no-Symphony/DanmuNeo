namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 剪贴板中的单条转发规则配置。
/// </summary>
public sealed class DanmuForwardRuleClipboardEntry
{
    /// <summary>
    /// 源工作区 ID。
    /// </summary>
    public string SourceWorkspaceId { get; set; } = "";

    /// <summary>
    /// 源直播间状态 ID。
    /// </summary>
    public string SourceRoomStateId { get; set; } = "";

    /// <summary>
    /// 转发发送账号 ID。
    /// </summary>
    public string AccountOverrideId { get; set; } = "";

    /// <summary>
    /// 转发符号组 ID。
    /// </summary>
    public string MarkSymbolGroupId { get; set; } = "";

    /// <summary>
    /// 匹配发送人的 UID。
    /// </summary>
    public string SenderUid { get; set; } = "";

    /// <summary>
    /// 匹配弹幕内容的正则表达式。
    /// </summary>
    public string ContentPattern { get; set; } = "";
}
