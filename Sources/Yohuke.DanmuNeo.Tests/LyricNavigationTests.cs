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

    private static LiveRoomTabViewModel CreateRoom(
        IDanmuSendService sendService, string lyricText = "[00:01]第一句\n[00:10]第二句")
    {
        var settings = new AppSettings();
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
