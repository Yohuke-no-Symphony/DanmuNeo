using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证草稿和双语歌词在部分成功后的续发与历史更新。</summary>
public class RoomSendingContinuationTests
{
    /// <summary>未编辑的失败草稿只补发尚未接受的正文并更新原历史。</summary>
    [AvaloniaFact]
    public async Task UneditedDraftContinuesAtFirstUnacceptedBodyRange()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        using var room = CreateRoom(transport, 6);
        room.InputDraft = "甲乙丙丁戊己庚辛";
        await room.SendDraftAsync();
        Assert.True(room.IsDraftSendFailed);
        Assert.Equal("甲乙丙丁戊己庚辛", room.InputDraft);

        await room.SendDraftAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("", room.InputDraft);
        Assert.False(room.IsDraftSendFailed);
        Assert.Single(transport.Messages, message => message.Contains("甲乙丙丁"));
        Assert.Equal(3, room.TranslateHistoryItems.Count);
        Assert.All(room.TranslateHistoryItems, record => Assert.True(record.IsSendAccepted));
        Assert.DoesNotContain(room.DanmuItems, record => record.IsLocalRecord);
    }

    /// <summary>编辑后恢复同样文字也视为新的发送操作。</summary>
    [AvaloniaFact]
    public async Task EditingDraftStartsNewOperationEvenWhenTextIsRestored()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        using var room = CreateRoom(transport, 6);
        room.InputDraft = "甲乙丙丁戊己";
        await room.SendDraftAsync();
        room.InputDraft = "修改";
        room.InputDraft = "甲乙丙丁戊己";

        await room.SendDraftAsync();

        Assert.Equal(2, transport.Messages.Count(message => message == "【甲乙丙丁】"));
    }

    /// <summary>手动双语失败停留当前行，续发时不重复已经接受的语言。</summary>
    [AvaloniaFact]
    public async Task FailedManualBilingualLineStaysActiveAndOnlyRetriesTranslation()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        using var room = CreateRoom(transport, 20);
        room.LyricInput = "[00:01]原文\n[00:10]下一句";
        room.TranslatedLyricInput = "[00:01]译文";
        room.ApplyLyric();
        var line = room.ActiveLyricLine;
        await room.SendCurrentLyricAsync();
        Assert.Same(line, room.ActiveLyricLine);
        Assert.False(line!.IsSent);

        await room.SendCurrentLyricAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["【♪原文】", "【♪译文】", "【♪译文】"], transport.Messages);
        Assert.True(line.IsSent);
        Assert.Same(room.Lyrics[1], room.ActiveLyricLine);
        Assert.Equal(2, room.TranslateHistoryItems.Count);
    }

    /// <summary>重新加载歌词会清除上一歌词会话的部分发送进度。</summary>
    [AvaloniaFact]
    public async Task ReloadingLyricsClearsOldPartialProgress()
    {
        var transport = new ScriptedDanmuSendTransport();
        transport.Results.Enqueue(new());
        transport.Results.Enqueue(new() { FailureKind = DanmuSendFailureKind.Blocked });
        using var room = CreateRoom(transport, 20);
        room.LyricInput = "[00:01]原文";
        room.TranslatedLyricInput = "[00:01]译文";
        room.ApplyLyric();
        await room.SendCurrentLyricAsync();
        room.ApplyLyric();

        await room.SendCurrentLyricAsync();

        Assert.Equal(2, transport.Messages.Count(message => message == "【♪原文】"));
    }

    private static LiveRoomTabViewModel CreateRoom(ScriptedDanmuSendTransport transport, int limit)
    {
        var settings = new AppSettings { SendIntervalMs = 0 };
        var account = new BilibiliAccount { Id = "account", Cookie = "SESSDATA=test;bili_jct=test;DedeUserID=42" };
        var config = new FakeBilibiliDanmuConfigService { Config = new() { Length = limit } };
        var sender = new DanmuSendService(danmuConfigService: config, transport: transport);
        var room = new LiveRoomTabViewModel(new() { RoomId = "100" });
        room.Configure(_ => account, (_, _) => account, () => settings,
            () => MarkSymbolService.CreateDefaultGroup(settings), () => [], _ => [], sender,
            new FakeRoomAvatarService(), () => Task.CompletedTask, danmuConfigService: config);
        return room;
    }
}
