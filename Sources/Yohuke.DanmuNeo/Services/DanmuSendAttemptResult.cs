namespace Yohuke.DanmuNeo.Services;

/// <summary>单次网络尝试的原始结果，不产生业务历史。</summary>
public sealed record DanmuSendAttemptResult
{
    /// <summary>发送结果分类。</summary>
    public DanmuSendFailureKind FailureKind { get; init; }
    /// <summary>接口返回的业务码。</summary>
    public int? ApiCode { get; init; } = 0;
    /// <summary>服务端错误文本。</summary>
    public string ErrorMessage { get; init; } = "";
    /// <summary>传输或本地异常。</summary>
    public Exception? Error { get; init; }
    internal string? StatusKey { get; init; }
}
