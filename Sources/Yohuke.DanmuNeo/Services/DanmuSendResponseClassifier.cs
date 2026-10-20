using System.Net;

namespace Yohuke.DanmuNeo.Services;

/// <summary>只用明确响应证据区分可重试限频和其他失败。</summary>
public static class DanmuSendResponseClassifier
{
    /// <summary>分类弹幕接口业务响应，内容屏蔽优先。</summary>
    public static DanmuSendFailureKind Classify(int code, string? message, string? msg)
    {
        if (message?.Trim() == "f" || msg?.Trim() == "f")
        {
            return DanmuSendFailureKind.Blocked;
        }
        if (code == 0)
        {
            return DanmuSendFailureKind.None;
        }
        if (IsExplicitLengthLimit(message) || IsExplicitLengthLimit(msg))
        {
            return DanmuSendFailureKind.LengthExceeded;
        }
        if (IsExplicitFrequencyLimit(message) || IsExplicitFrequencyLimit(msg))
        {
            return DanmuSendFailureKind.RateLimited;
        }
        return DanmuSendFailureKind.Rejected;
    }

    /// <summary>分类 HTTP 错误，不将未知传输错误当成明确拒绝。</summary>
    public static DanmuSendFailureKind ClassifyHttp(HttpStatusCode? statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests
            ? DanmuSendFailureKind.RateLimited
            : DanmuSendFailureKind.OutcomeUnknown;
    }

    private static bool IsExplicitFrequencyLimit(string? text)
    {
        return text is not null &&
               (text.Contains("频率过快", StringComparison.Ordinal) || text.Contains("频率过高", StringComparison.Ordinal));
    }

    private static bool IsExplicitLengthLimit(string? text)
    {
        return text?.Contains("超出限制长度", StringComparison.Ordinal) == true;
    }
}
