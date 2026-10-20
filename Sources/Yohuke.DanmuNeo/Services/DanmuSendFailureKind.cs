namespace Yohuke.DanmuNeo.Services;

/// <summary>决定发送重试策略的终端结果分类。</summary>
public enum DanmuSendFailureKind
{
    /// <summary>接口已接受。</summary>
    None,
    /// <summary>服务端明确返回内容屏蔽。</summary>
    Blocked,
    /// <summary>服务端明确返回发送频率限制。</summary>
    RateLimited,
    /// <summary>其他业务拒绝。</summary>
    Rejected,
    /// <summary>无法确认是否已送达。</summary>
    OutcomeUnknown,
    /// <summary>请求未发送的本地失败。</summary>
    LocalFailure,
    /// <summary>服务端明确拒绝超出允许长度的分段。</summary>
    LengthExceeded
}
