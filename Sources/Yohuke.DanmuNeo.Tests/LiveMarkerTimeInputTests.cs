using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证累计小时输入、分隔符及严格的前缀边界。</summary>
public sealed class LiveMarkerTimeInputTests
{
    /// <summary>完整时间和前缀均接受半角、全角冒号与点，小时不在 24 回绕。</summary>
    [Theory]
    [InlineData("25:02:03")]
    [InlineData("25：02：03")]
    [InlineData("25.02.03")]
    [InlineData("25．02．03")]
    [InlineData("25：02.03")]
    public void SupportedSeparatorsKeepTotalHours(string text)
    {
        var expected = TimeSpan.FromHours(25) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3);
        Assert.True(LiveMarkerTimeInput.TryParse(text, out var parsed));
        Assert.Equal(expected, parsed);
        Assert.True(LiveMarkerTimeInput.TryReadPrefix(text + "  记录备注", out var prefix, out var remaining));
        Assert.Equal(expected, prefix);
        Assert.Equal("记录备注", remaining);
        Assert.StartsWith("25:02:03", LiveMarkerTimeInput.Format(parsed));
    }

    /// <summary>小数秒按毫秒精度解析，不把前缀后的内容吞入时间。</summary>
    [Theory]
    [InlineData("01:02:03.4", 400)]
    [InlineData("01:02:03.45", 450)]
    [InlineData("01:02:03.456", 456)]
    [InlineData("01:02:03,4", 400)]
    public void FractionalSecondsUseMilliseconds(string text, int milliseconds)
    {
        Assert.True(LiveMarkerTimeInput.TryReadPrefix(text + " content", out var time, out var remaining));
        Assert.Equal(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3) +
            TimeSpan.FromMilliseconds(milliseconds), time);
        Assert.Equal("content", remaining);
    }

    /// <summary>完整解析拒绝负值、非法分秒、溢出及多余正文。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-01:00:00")]
    [InlineData("25:60:00")]
    [InlineData("25:00:60")]
    [InlineData("999999999999999999999:00:00")]
    [InlineData("25:00:00 content")]
    [InlineData("25:00:00.1234")]
    public void InvalidFullTimeIsRejected(string? text)
    {
        Assert.False(LiveMarkerTimeInput.TryParse(text, out _));
    }

    /// <summary>时间必须从文本开头开始并在正文前保留空白边界。</summary>
    [Theory]
    [InlineData("event 25:00:00")]
    [InlineData("25:00:00event")]
    [InlineData("25:00:001 event")]
    [InlineData("-25:00:00 event")]
    public void InvalidPrefixLeavesInputUntouched(string text)
    {
        Assert.False(LiveMarkerTimeInput.TryReadPrefix(text, out _, out var remaining));
        Assert.Equal(text, remaining);
    }

    /// <summary>格式化保留超过一天的总小时数。</summary>
    [Fact]
    public void FormatDoesNotWrapAtOneDay()
    {
        var value = TimeSpan.FromHours(49) + TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(34) + TimeSpan.FromMilliseconds(567);
        Assert.Equal("49:12:34.567", LiveMarkerTimeInput.Format(value));
    }
}
