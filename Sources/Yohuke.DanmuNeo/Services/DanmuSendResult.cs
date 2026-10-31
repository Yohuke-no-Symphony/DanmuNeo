using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 弹幕接口发送结果。
/// </summary>
public class DanmuSendResult
{
    private readonly string errorMessage = "";

    /// <summary>用于限频重试与失败反馈的结果分类。</summary>
    public DanmuSendFailureKind FailureKind { get; init; }

    /// <summary>本轮发送该正文范围时执行的网络尝试次数。</summary>
    public int AttemptCount { get; init; }

    internal string? ErrorMessageKey { get; init; }
    internal object[] ErrorMessageArguments { get; init; } = [];
    internal Func<string>? ErrorMessageProvider { get; init; }

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
    public string ErrorMessage
    {
        get => ErrorMessageKey is { } key
            ? Localization.Format(key, ErrorMessageArguments)
            : ErrorMessageProvider?.Invoke() ?? errorMessage;
        init => errorMessage = value;
    }

    /// <summary>
    /// 本地或传输失败的原始异常，供日志保留完整诊断详情。
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>最终网络尝试的请求摘要与脱敏响应。</summary>
    public DanmuSendDiagnostic? Diagnostic { get; init; }

    /// <summary>
    /// 对应的本地发送记录。
    /// </summary>
    public DanmuFeedItem Record { get; init; } = new();
}
