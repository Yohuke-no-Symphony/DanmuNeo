using System.Net;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>发送策略与兼容行为的离线回归测试。</summary>
public class DanmuRetryClassificationTests
{
    /// <summary>只有非零业务码和明确频率文本共同出现才识别为限频。</summary>
    [Theory]
    [InlineData(0, "f", DanmuSendFailureKind.Blocked)]
    [InlineData(10030, "风控拒绝", DanmuSendFailureKind.Rejected)]
    [InlineData(10030, null, DanmuSendFailureKind.Rejected)]
    [InlineData(999, "发送频率过快，请稍后再试", DanmuSendFailureKind.RateLimited)]
    [InlineData(999, "发送频率过高", DanmuSendFailureKind.RateLimited)]
    [InlineData(0, "发送频率过快", DanmuSendFailureKind.None)]
    [InlineData(10020, "超出限制长度", DanmuSendFailureKind.LengthExceeded)]
    [InlineData(0, "超出限制长度", DanmuSendFailureKind.None)]
    public void BusinessClassificationRequiresExplicitFrequencyResponse(int code, string? message, DanmuSendFailureKind expected)
    {
        Assert.Equal(expected, DanmuSendResponseClassifier.Classify(code, message, null));
    }

    /// <summary>屏蔽标记优先于频率文本，HTTP 429 单独归为限频。</summary>
    [Fact]
    public void BlockMarkerWinsOverFrequencyText()
    {
        Assert.Equal(DanmuSendFailureKind.Blocked,
            DanmuSendResponseClassifier.Classify(999, "发送频率过快", "f"));
        Assert.Equal(DanmuSendFailureKind.RateLimited,
            DanmuSendResponseClassifier.ClassifyHttp(HttpStatusCode.TooManyRequests));
    }
}
