using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

public class LyricTimelineServiceTests
{
    [Fact]
    public void ParseReadsTimelineLines()
    {
        var service = new LyricTimelineService();

        var lines = service.Parse("[00:01.50]第一句\n[00:03.00]第二句");

        Assert.Equal(2, lines.Count);
        Assert.Equal(1.5, lines[0].TimeSeconds);
        Assert.Equal("第一句", lines[0].Content);
        Assert.True(LyricTimelineService.HasTimeline(lines));
    }

    [Fact]
    public void ParseKeepsPlainLyricsWithoutTimeline()
    {
        var service = new LyricTimelineService();

        var lines = service.Parse("第一句\n第二句");

        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal(-1, line.TimeSeconds));
        Assert.False(LyricTimelineService.HasTimeline(lines));
    }

    [Fact]
    public void CreateMessageUsesConfiguredMarks()
    {
        var message = LyricTimelineService.CreateMessage("【♪", "】", "歌词");

        Assert.Equal("【♪歌词】", message);
    }

    [Fact]
    public void ParseMultilingualMergesTimedLyricsByTimestamp()
    {
        var service = new LyricTimelineService();

        var lines = service.ParseMultilingual(
            "[00:01.00]原文一\n[00:03.00]原文二",
            "[00:01.00]翻译一\n[00:03.00]翻译二");

        Assert.Equal(2, lines.Count);
        Assert.Equal("原文一", lines[0].Content);
        Assert.Equal("翻译一", lines[0].TranslatedContent);
        Assert.Equal("翻译二", lines[1].TranslatedContent);
    }

    [Fact]
    public void ParseMultilingualAlignsPlainLyricsByLineOrder()
    {
        var service = new LyricTimelineService();

        var lines = service.ParseMultilingual("原文一\n原文二", "译文一\n译文二");

        Assert.Equal("原文一", lines[0].Content);
        Assert.Equal("译文一", lines[0].TranslatedContent);
        Assert.Equal("译文二", lines[1].TranslatedContent);
    }

    [Theory]
    [InlineData("原文", "译文", "原文 / 译文")]
    [InlineData("原文", "", "原文")]
    [InlineData("", "译文", "译文")]
    [InlineData("相同", "相同", "相同")]
    public void CreateMultilingualContentCombinesLanguageText(string content, string translated, string expected)
    {
        Assert.Equal(expected, LyricTimelineService.CreateMultilingualContent(content, translated));
    }

    [Theory]
    [InlineData(LyricSendMode.Bilingual, "原文 / 译文")]
    [InlineData(LyricSendMode.OriginalOnly, "原文")]
    [InlineData(LyricSendMode.TranslationOnly, "译文")]
    public void CreateContentUsesSelectedMode(LyricSendMode mode, string expected)
    {
        Assert.Equal(expected, LyricTimelineService.CreateContent(" 原文 ", " 译文 ", mode));
    }

    /// <summary>
    /// 发送内容按语言独立输出，并保持单语模式、空内容和相同文本的处理规则。
    /// </summary>
    [Theory]
    [InlineData(LyricSendMode.Bilingual, " 原文 ", " 译文 ", new[] { "原文", "译文" })]
    [InlineData(LyricSendMode.Bilingual, "原文 / 原文", "译文", new[] { "原文 / 原文", "译文" })]
    [InlineData(LyricSendMode.Bilingual, "原文", " \t", new[] { "原文" })]
    [InlineData(LyricSendMode.Bilingual, " \t", "译文", new[] { "译文" })]
    [InlineData(LyricSendMode.Bilingual, " 相同 ", "相同", new[] { "相同" })]
    [InlineData(LyricSendMode.Bilingual, " \t", "", new string[0])]
    [InlineData(LyricSendMode.OriginalOnly, " 原文 ", "译文", new[] { "原文" })]
    [InlineData(LyricSendMode.OriginalOnly, "", "译文", new string[0])]
    [InlineData(LyricSendMode.TranslationOnly, "原文", " 译文 ", new[] { "译文" })]
    [InlineData(LyricSendMode.TranslationOnly, "原文", "", new string[0])]
    public void CreateSendContentsSeparatesLanguages(
        LyricSendMode mode, string content, string translated, string[] expected)
    {
        Assert.Equal(expected, LyricTimelineService.CreateSendContents(content, translated, mode));
    }
}
