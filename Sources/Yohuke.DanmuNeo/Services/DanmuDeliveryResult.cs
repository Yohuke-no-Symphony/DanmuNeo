namespace Yohuke.DanmuNeo.Services;

/// <summary>包含已接受范围和最终失败原因的逻辑投递结果。</summary>
public sealed record DanmuDeliveryResult
{
    /// <summary>供相同请求继续发送的进度。</summary>
    public required DanmuDeliveryProgress Progress { get; init; }
    /// <summary>逻辑消息的全部分段是否发送完成。</summary>
    public bool IsComplete => Progress.IsComplete && Results.Count > 0 && Results.All(result => result.IsAccepted);
    /// <summary>各语言和分段的最终结果。</summary>
    public IReadOnlyList<DanmuSendResult> Results => Progress.Results;
}
