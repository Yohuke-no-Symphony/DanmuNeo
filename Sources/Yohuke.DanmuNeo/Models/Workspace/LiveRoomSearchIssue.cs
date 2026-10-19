using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>某一查找来源的失败，允许与其他来源的成功结果一起返回。</summary>
public class LiveRoomSearchIssue
{
    private string message = "";

    /// <summary>失败来源。</summary>
    public LiveRoomSearchSource Source { get; init; }

    /// <summary>服务端业务错误码；传输失败时为空。</summary>
    public int? ApiCode { get; init; }

    /// <summary>本地错误的语义键；远端原文不设置此项。</summary>
    public string? MessageKey { get; init; }

    /// <summary>可读错误，不包含请求凭据。</summary>
    public string Message
    {
        get => MessageKey is null ? message : Localization.Get(MessageKey);
        init => message = value;
    }

    /// <summary>带查找来源的错误说明。</summary>
    public string DisplayMessage => Localization.Format("Room.Search.Issue", LiveRoomSearchResult.GetSourceLabel(Source), Message);
}
