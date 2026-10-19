namespace Yohuke.DanmuNeo.Services;

/// <summary>对明确限频提供有界重试，不重复发送结果不明的请求。</summary>
public static class DanmuSendRetryPolicy
{
    /// <summary>同一实际分段允许的额外重试次数。</summary>
    public const int MAX_RETRIES = 2;

    /// <summary>计算下一次限频重试的等待时间；不允许重试时返回空。</summary>
    public static TimeSpan? GetDelay(DanmuSendFailureKind failureKind, bool enabled, int attemptCount, int intervalMs)
    {
        if (!enabled || failureKind != DanmuSendFailureKind.RateLimited || attemptCount > MAX_RETRIES)
        {
            return null;
        }
        return TimeSpan.FromMilliseconds(Math.Max(intervalMs, 1000L) * (1L << (attemptCount - 1)));
    }
}
