using System.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 歌词手动跳转与发送的回归测试。
/// </summary>
public class LyricNavigationTests
{
    /// <summary>
    /// 暂停时跳到下一句，发送目标句的双语内容并停留在句首。
    /// </summary>
    [Fact]
    public async Task NextLineSendsTargetAndKeepsPlaybackPaused()
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService);
        room.TranslatedLyricInput = "[00:01]第一句翻译\n[00:10]第二句翻译";
        room.ApplyLyric();
        room.AdjustLyricPlaybackPosition(2);

        await room.SeekNextLyricAsync();

        Assert.Equal(["【♪第二句】", "【♪第二句翻译】"], sendService.SentMessages);
        Assert.Same(room.Lyrics[1], room.ActiveLyricLine);
        Assert.Equal(10, room.LyricPlaybackPositionSeconds);
        Assert.All(room.Lyrics, line => Assert.Equal(0, line.Progress));
        Assert.False(room.Lyrics[0].IsSent);
        Assert.True(room.Lyrics[1].IsSent);
        Assert.False(room.IsLyricAutoSending);
    }

    /// <summary>
    /// 空列表和最后一句禁用下一句，调用也不会发送或改变位置。
    /// </summary>
    [Fact]
    public async Task NextLineStopsAtLastLineAndHandlesEmptyLyrics()
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService);
        Assert.True(room.SeekNextLyricCommand.CanExecute(null));
        await room.SeekNextLyricCommand.ExecuteAsync(null);
        Assert.False(room.SeekNextLyricCommand.CanExecute(null));
        await room.SeekNextLyricAsync();
        Assert.Single(sendService.SentMessages);
        Assert.Equal(10, room.LyricPlaybackPositionSeconds);

        room.ClearLyric();
        Assert.False(room.SeekNextLyricCommand.CanExecute(null));
        await room.SeekNextLyricAsync();
        Assert.Single(sendService.SentMessages);
        Assert.Null(room.ActiveLyricLine);
        Assert.Equal(0, room.LyricPlaybackPositionSeconds);

        room.LyricInput = "[00:01]新第一句\n[00:02]新第二句";
        room.ApplyLyric();
        Assert.True(room.SeekNextLyricCommand.CanExecute(null));
    }

    /// <summary>
    /// 暂停时可以定位并发送首句、末句以及没有时间戳的歌词。
    /// </summary>
    [Theory]
    [InlineData("[00:01]第一句\n[00:10]第二句", 0, 1, "第一句")]
    [InlineData("[00:01]第一句\n[00:10]第二句", 1, 10, "第二句")]
    [InlineData("第一句\n第二句", 1, 0, "第二句")]
    public async Task SeekAndSendTargetsRequestedLineWhilePaused(
        string lyricText, int index, double expectedPosition, string expectedContent)
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService, lyricText);

        await room.SeekAndSendLyricLineAsync(room.Lyrics[index]);

        Assert.Equal([$"【♪{expectedContent}】"], sendService.SentMessages);
        Assert.Same(room.Lyrics[index], room.ActiveLyricLine);
        Assert.Equal(expectedPosition, room.LyricPlaybackPositionSeconds);
        Assert.True(room.Lyrics[index].IsSent);
        Assert.False(room.IsLyricAutoSending);
    }

    /// <summary>
    /// 同时间戳歌词按目标行定位，下一句不会跳过相邻行。
    /// </summary>
    [Fact]
    public async Task DuplicateTimestampsKeepRequestedLineActive()
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService, "[00:01]第一句\n[00:01]第二句\n[00:10]第三句");

        await room.SeekAndSendLyricLineAsync(room.Lyrics[0]);
        Assert.Same(room.Lyrics[0], room.ActiveLyricLine);
        await room.SeekNextLyricAsync();

        Assert.Same(room.Lyrics[1], room.ActiveLyricLine);
        Assert.Equal(1, room.LyricPlaybackPositionSeconds);
        Assert.Equal(["【♪第一句】", "【♪第二句】"], sendService.SentMessages);
    }

    /// <summary>
    /// 无时间戳歌词也可以按行逐句跳转和发送。
    /// </summary>
    [Fact]
    public async Task NextLineSupportsUntimedLyrics()
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService, "第一句\n第二句\n第三句");

        await room.SeekNextLyricAsync();
        await room.SeekNextLyricAsync();
        await room.SeekNextLyricAsync();

        Assert.Same(room.Lyrics[2], room.ActiveLyricLine);
        Assert.Equal(["【♪第二句】", "【♪第三句】"], sendService.SentMessages);
        Assert.False(room.IsLyricAutoSending);
    }

    /// <summary>
    /// 无效目标不会修改当前歌词，也不会发送。
    /// </summary>
    [Fact]
    public async Task SeekAndSendIgnoresNullAndForeignLines()
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService);
        room.AdjustLyricPlaybackPosition(2);

        await room.SeekAndSendLyricLineAsync(null);
        await room.SeekAndSendLyricLineAsync(new LyricLineState { TimeSeconds = 10, Content = "外部行" });

        Assert.Empty(sendService.SentMessages);
        Assert.Same(room.Lyrics[0], room.ActiveLyricLine);
        Assert.Equal(3, room.LyricPlaybackPositionSeconds);
    }

    /// <summary>
    /// 手动跳转遵从防重复设置。
    /// </summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task SeekAndSendHonorsRepeatedSendPreference(bool preventRepeatedSend, int expectedCount)
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService);
        room.PreventRepeatedLyricSend = preventRepeatedSend;

        await room.SeekAndSendLyricLineAsync(room.Lyrics[1]);
        await room.SeekAndSendLyricLineAsync(room.Lyrics[1]);

        Assert.Equal(expectedCount, sendService.SentMessages.Count);
        Assert.Same(room.Lyrics[1], room.ActiveLyricLine);
    }

    /// <summary>
    /// 播放中手动跳转立即发送，自动循环不会再次发送同一次进入的目标行。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NextLineDuringPlaybackSendsOnceAndKeepsPlaying(bool preventRepeatedSend)
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService);
        room.TranslatedLyricInput = "[00:01]第一句翻译\n[00:10]第二句翻译";
        room.ApplyLyric();
        room.PreventRepeatedLyricSend = preventRepeatedSend;
        var playback = room.StartAutoLyricAsync();

        try
        {
            await room.SeekNextLyricAsync();
            await Task.Delay(120);

            Assert.Equal(["【♪第一句】", "【♪第一句翻译】", "【♪第二句】", "【♪第二句翻译】"], sendService.SentMessages);
            Assert.Same(room.Lyrics[1], room.ActiveLyricLine);
            Assert.True(room.IsLyricAutoSending);
            Assert.True(room.LyricPlaybackPositionSeconds >= 10);
        }
        finally
        {
            room.StopAutoLyric();
            await playback;
        }
    }

    /// <summary>
    /// 混合时间轴播放中跳到无时间戳行后，计时更新不会退回或重发上一句。
    /// </summary>
    [Fact]
    public async Task UntimedTargetStaysActiveDuringPlayback()
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService, "[00:01]第一句\n第二句");
        room.PreventRepeatedLyricSend = false;
        var playback = room.StartAutoLyricAsync();

        try
        {
            await room.SeekNextLyricAsync();
            await Task.Delay(120);

            Assert.Equal(["【♪第一句】", "【♪第二句】"], sendService.SentMessages);
            Assert.Same(room.Lyrics[1], room.ActiveLyricLine);
            Assert.True(room.IsLyricAutoSending);
        }
        finally
        {
            room.StopAutoLyric();
            await playback;
        }
    }

    /// <summary>
    /// 手动或自动发送尚未完成时，再次定位同一行不会产生并行重复请求。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightLineIsNotSentAgain(bool startWithPlayback)
    {
        var sendService = new BlockingDanmuSendService();
        var recordCount = 0;
        sendService.RecordCreated += (_, _) => Interlocked.Increment(ref recordCount);
        var room = CreateRoom(sendService);
        room.TranslatedLyricInput = "[00:01]第一句翻译";
        room.ApplyLyric();
        room.PreventRepeatedLyricSend = false;
        var first = startWithPlayback
            ? room.StartAutoLyricAsync()
            : room.SeekAndSendLyricLineAsync(room.Lyrics[0]);

        try
        {
            await sendService.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(1));
            await room.SeekAndSendLyricLineAsync(room.Lyrics[0]);
            Assert.Equal(0, Volatile.Read(ref recordCount));
            sendService.ReleaseFirstSend();

            if (startWithPlayback)
            {
                await Task.Delay(120);
                Assert.True(room.IsLyricAutoSending);
            }
            else
            {
                await first;
            }

            Assert.Equal(2, Volatile.Read(ref recordCount));
        }
        finally
        {
            room.StopAutoLyric();
            sendService.ReleaseFirstSend();
            await first;
        }
    }

    /// <summary>
    /// 停止自动播放后，尚未发出的译文不再发送，也不把部分发送的行标记为完成。
    /// </summary>
    [Fact]
    public async Task StoppingAutoPlaybackPreventsPendingTranslation()
    {
        var sendService = new BlockingDanmuSendService();
        var messages = new List<string>();
        sendService.RecordCreated += (_, record) => messages.Add(record.Content);
        var room = CreateRoom(sendService);
        room.TranslatedLyricInput = "[00:01]第一句翻译";
        room.ApplyLyric();
        var playback = room.StartAutoLyricAsync();

        try
        {
            await sendService.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(1));
            room.StopAutoLyric();
            sendService.ReleaseFirstSend();
            await playback;

            Assert.Equal(["【♪第一句】"], messages);
            Assert.False(room.Lyrics[0].IsSent);
            Assert.False(room.IsLyricAutoSending);
        }
        finally
        {
            room.StopAutoLyric();
            sendService.ReleaseFirstSend();
            await playback;
        }
    }

    /// <summary>
    /// 重载或清空歌词后，旧发送完成不会改变新会话的定位或发送标记。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldSendDoesNotChangeReloadedSession(bool clearLyrics)
    {
        var sendService = new BlockingDanmuSendService();
        var room = CreateRoom(sendService);
        var pending = room.SeekAndSendLyricLineAsync(room.Lyrics[1]);

        try
        {
            await sendService.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(1));

            if (clearLyrics)
            {
                room.ClearLyric();
            }
            else
            {
                room.LyricInput = "[00:02]新歌词";
                room.ApplyLyric();
            }

            sendService.ReleaseFirstSend();
            await pending;

            Assert.Equal(clearLyrics ? 0 : 2, room.LyricPlaybackPositionSeconds);
            Assert.All(room.Lyrics, line => Assert.False(line.IsSent));
            Assert.False(room.IsLyricAutoSending);
        }
        finally
        {
            sendService.ReleaseFirstSend();
            await pending;
        }
    }

    /// <summary>
    /// 接口拒绝发送时保留重试机会，不把歌词标记为已发送。
    /// </summary>
    [Fact]
    public async Task RejectedSendCanBeRetriedWithRepeatPreventionEnabled()
    {
        var sendService = new FakeDanmuSendService { IsAccepted = false };
        var room = CreateRoom(sendService);
        await room.SeekAndSendLyricLineAsync(room.Lyrics[1]);
        Assert.False(room.Lyrics[1].IsSent);

        sendService.IsAccepted = true;
        await room.SeekAndSendLyricLineAsync(room.Lyrics[1]);

        Assert.Equal(2, sendService.SentMessages.Count);
        Assert.True(room.Lyrics[1].IsSent);
    }

    /// <summary>
    /// 下一句和指定行发送都跳过连续空行，并按当前语言模式选择有效内容。
    /// </summary>
    [Theory]
    [InlineData(LyricSendMode.Bilingual, false, new[] { "【♪原文】", "【♪译文】" })]
    [InlineData(LyricSendMode.OriginalOnly, false, new[] { "【♪原文】" })]
    [InlineData(LyricSendMode.TranslationOnly, false, new[] { "【♪译文】" })]
    [InlineData(LyricSendMode.Bilingual, true, new[] { "【♪原文】", "【♪译文】" })]
    public async Task ManualNavigationSkipsEmptyLines(LyricSendMode mode, bool seekExplicitly, string[] expected)
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService, "[00:01]当前句\n[00:02]\n[00:03] \t\n[00:04]原文\n[00:05]");
        room.TranslatedLyricInput = "[00:04]译文";
        room.ApplyLyric();
        room.LyricSendMode = mode;

        if (seekExplicitly)
        {
            await room.SeekAndSendLyricLineAsync(room.Lyrics[1]);
        }
        else
        {
            await room.SeekNextLyricAsync();
        }

        Assert.Equal(expected, sendService.SentMessages);
        Assert.Same(room.Lyrics[3], room.ActiveLyricLine);
        Assert.Equal(4, room.LyricPlaybackPositionSeconds);
        Assert.True(room.Lyrics[3].IsSent);
        Assert.False(room.Lyrics[1].IsSent);
        Assert.False(room.Lyrics[2].IsSent);
        Assert.False(room.SeekNextLyricCommand.CanExecute(null));
        Assert.Equal(5, room.Lyrics.Count);
    }

    /// <summary>
    /// 发送当前句时略过开头空行，并将下一次发送位置推进到下一条非空歌词。
    /// </summary>
    [Fact]
    public async Task CurrentSendSkipsEmptyLinesBeforeAndAfterContent()
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService, "[00:01]\n[00:02] \t\n[00:03]第一句\n[00:04]\n[00:05]第二句\n[00:06]");

        await room.SendCurrentLyricAsync();

        Assert.Equal(["【♪第一句】"], sendService.SentMessages);
        Assert.Same(room.Lyrics[4], room.ActiveLyricLine);
        Assert.Equal(5, room.LyricPlaybackPositionSeconds);
        Assert.True(room.Lyrics[2].IsSent);
        Assert.False(room.Lyrics[4].IsSent);

        await room.SendCurrentLyricAsync();

        Assert.Equal(["【♪第一句】", "【♪第二句】"], sendService.SentMessages);
        Assert.Same(room.Lyrics[4], room.ActiveLyricLine);
        Assert.False(room.Lyrics[5].IsSent);
    }

    /// <summary>
    /// 全为空行时所有手动发送入口都不产生请求，也不把空行标记为已发送。
    /// </summary>
    [Fact]
    public async Task AllEmptyLinesDoNotSendOrAdvance()
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService, "[00:01]\n[00:02] \t");

        Assert.False(room.SeekNextLyricCommand.CanExecute(null));
        await room.SendCurrentLyricAsync();
        await room.SeekNextLyricAsync();
        await room.SeekAndSendLyricLineAsync(room.Lyrics[1]);

        Assert.Empty(sendService.SentMessages);
        Assert.All(room.Lyrics, line => Assert.False(line.IsSent));
        Assert.Same(room.Lyrics[0], room.ActiveLyricLine);
        Assert.Equal(1, room.LyricPlaybackPositionSeconds);
    }

    /// <summary>
    /// 切换语言模式会刷新下一句命令，使只有译文的目标行可以立即发送。
    /// </summary>
    [Fact]
    public async Task ModeChangeRefreshesNextSendableLine()
    {
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService, "[00:01]原文\n[00:02]");
        room.TranslatedLyricInput = "[00:02]译文";
        room.ApplyLyric();
        room.LyricSendMode = LyricSendMode.OriginalOnly;
        Assert.False(room.SeekNextLyricCommand.CanExecute(null));
        var notified = false;
        room.SeekNextLyricCommand.CanExecuteChanged += (_, _) => notified = true;

        room.LyricSendMode = LyricSendMode.TranslationOnly;

        Assert.True(notified);
        Assert.True(room.SeekNextLyricCommand.CanExecute(null));
        await room.SeekNextLyricAsync();
        Assert.Equal(["【♪译文】"], sendService.SentMessages);
    }

    /// <summary>
    /// 替换规则清空的歌词也会被手动导航跳过。
    /// </summary>
    [Fact]
    public async Task ManualNavigationSkipsReplacementClearedLines()
    {
        var settings = new AppSettings
        {
            ShieldReplacementRules =
            [
                new()
                {
                    SourceText = "删除",
                    ReplacementText = " \t"
                }
            ]
        };
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService, "[00:01]当前句\n[00:02]删除\n[00:03]有效句", settings);

        await room.SeekNextLyricAsync();

        Assert.Equal(["【♪有效句】"], sendService.SentMessages);
        Assert.Same(room.Lyrics[2], room.ActiveLyricLine);
        Assert.False(room.Lyrics[1].IsSent);
    }

    /// <summary>
    /// 自动播放不发送替换后为空的语言，也不产生只有歌词符号的弹幕。
    /// </summary>
    [Theory]
    [InlineData("删除", new string[0])]
    [InlineData("有效译文", new[] { "【♪有效译文】" })]
    public async Task AutoPlaybackSkipsReplacementClearedContent(string translation, string[] expected)
    {
        var settings = new AppSettings
        {
            ShieldReplacementRules =
            [
                new()
                {
                    SourceText = "删除",
                    ReplacementText = ""
                }
            ]
        };
        var sendService = new FakeDanmuSendService();
        var room = CreateRoom(sendService, "[00:01]删除", settings);
        room.TranslatedLyricInput = $"[00:01]{translation}";
        room.ApplyLyric();
        var playback = room.StartAutoLyricAsync();

        try
        {
            Assert.Equal(expected, sendService.SentMessages);
            Assert.Equal(expected.Length > 0, room.Lyrics[0].IsSent);
        }
        finally
        {
            room.StopAutoLyric();
            await playback;
        }
    }

    /// <summary>
    /// 自动播放经过空白间奏时保留时间轴，只有到达有效句后才发送。
    /// </summary>
    [Fact]
    public async Task AutoPlaybackPreservesEmptyLineTiming()
    {
        var sendService = new FakeDanmuSendService();
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sendService.RecordCreated += (_, _) => sent.TrySetResult();
        var room = CreateRoom(sendService, "[00:01]\n[00:10]有效句");
        var playback = room.StartAutoLyricAsync();

        try
        {
            await Task.Delay(120);
            Assert.Empty(sendService.SentMessages);
            Assert.Same(room.Lyrics[0], room.ActiveLyricLine);
            Assert.InRange(room.LyricPlaybackPositionSeconds, 1, 9);
            Assert.False(room.Lyrics[0].IsSent);

            room.SeekLyricLineStart(room.Lyrics[1]);
            await sent.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(["【♪有效句】"], sendService.SentMessages);
        }
        finally
        {
            room.StopAutoLyric();
            await playback;
        }
    }

    private static LiveRoomTabViewModel CreateRoom(
        IDanmuSendService sendService, string lyricText = "[00:01]第一句\n[00:10]第二句", AppSettings? settings = null)
    {
        settings ??= new();
        var room = new LiveRoomTabViewModel(new() { RoomId = "100", LyricText = lyricText });
        room.Configure(
            _ => null,
            (_, _) => null,
            () => settings,
            () => MarkSymbolService.CreateDefaultGroup(settings),
            () => [],
            _ => [],
            sendService,
            new FakeRoomAvatarService(),
            () => Task.CompletedTask);
        return room;
    }
}
