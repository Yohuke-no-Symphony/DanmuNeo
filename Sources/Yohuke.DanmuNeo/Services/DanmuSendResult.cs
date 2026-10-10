using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 弹幕接口发送结果。
/// </summary>
public class DanmuSendResult
{
    /// <summary>
    /// 是否被 B 站接口接受。
    /// </summary>
    public bool IsAccepted { get; init; }

    /// <summary>
    /// 已收到的接口业务状态码；本地或传输失败时为空。
    /// </summary>
    public int? ApiCode { get; init; }

    /// <summary>
    /// 接口是否明确拒绝本次发送。
    /// </summary>
    public bool IsExplicitlyRejected => !IsAccepted && ApiCode is not null and not 0;

    /// <summary>
    /// 接口返回的错误信息。
    /// </summary>
    public string ErrorMessage { get; init; } = "";

    /// <summary>
    /// 本次接口请求开始时间。
    /// </summary>
    public DateTimeOffset RequestedAt { get; init; }

    /// <summary>
    /// 对应的本地发送记录。
    /// </summary>
    public DanmuFeedItem Record { get; init; } = new();
}
