using Avalonia.Media;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

public class LiveRoomTabViewModelTests
{
    [Fact]
    public void RepeatedSuperChatIdIsDisplayedOnce()
    {
        var viewModel = new LiveRoomTabViewModel(new());
        var message = CreateSuperChatMessage("sc-1");

        viewModel.AddSuperChat(message);
        viewModel.AddSuperChat(message);

        var item = Assert.Single(viewModel.SuperChats);
        Assert.Equal("sc-1", item.MessageId);
        Assert.Equal("￥30", item.PriceText);
        Assert.Equal(Color.Parse("#3171D2"), item.BorderColor);
    }

    [Fact]
    public void DifferentSuperChatIdsAreDisplayedSeparately()
    {
        var viewModel = new LiveRoomTabViewModel(new());

        viewModel.AddSuperChat(CreateSuperChatMessage("sc-1"));
        viewModel.AddSuperChat(CreateSuperChatMessage("sc-2"));

        Assert.Equal(2, viewModel.SuperChats.Count);
    }

    [Fact]
    public void SuperChatWithoutIdUsesFallbackIdentity()
    {
        var viewModel = new LiveRoomTabViewModel(new());
        var message = CreateSuperChatMessage("");

        viewModel.AddSuperChat(message);
        viewModel.AddSuperChat(CreateSuperChatMessage(""));

        Assert.Single(viewModel.SuperChats);
    }

    [Fact]
    public void SuperChatInvalidColorUsesBilibiliBlue()
    {
        var viewModel = new LiveRoomTabViewModel(new());
        var message = CreateSuperChatMessage("sc-1");
        message.BorderColor = "invalid";

        viewModel.AddSuperChat(message);

        Assert.Equal(Color.Parse("#2A60B2"), Assert.Single(viewModel.SuperChats).BorderColor);
    }

    [Fact]
    public void SuperChatDeduplicationHistoryIsBounded()
    {
        var viewModel = new LiveRoomTabViewModel(new());

        for (var index = 0; index <= 512; index++)
        {
            viewModel.AddSuperChat(CreateSuperChatMessage($"sc-{index}"));
        }

        viewModel.AddSuperChat(CreateSuperChatMessage("sc-0"));

        Assert.Equal(100, viewModel.SuperChats.Count);
        Assert.Equal("sc-0", viewModel.SuperChats[0].MessageId);
    }

    [Fact]
    public void CopySuperChatInsertsWrappedContentToInputDraft()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState());

        viewModel.CopySuperChat(new SuperChatItem
        {
            Content = "弹幕内容"
        });

        Assert.Equal("\"弹幕内容\"", viewModel.InputDraft);
    }

    [Fact]
    public void InsertDanmuContentWritesWrappedContentToEmptyDraft()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState());

        var nextCaretIndex = viewModel.InsertDanmuContent("弹幕内容");

        Assert.Equal("\"弹幕内容\"", viewModel.InputDraft);
        Assert.Equal(viewModel.InputDraft.Length, nextCaretIndex);
    }

    [Fact]
    public void InsertDanmuContentInsertsAtSpecifiedPositionWithoutReplacing()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState())
        {
            InputDraft = "开头结尾"
        };

        var nextCaretIndex = viewModel.InsertDanmuContent("弹幕内容", 2);

        Assert.Equal("开头\"弹幕内容\"结尾", viewModel.InputDraft);
        Assert.Equal("开头\"弹幕内容\"".Length, nextCaretIndex);
    }

    [Fact]
    public void InsertDanmuContentUsesCustomMarks()
    {
        var settings = new AppSettings
        {
            DanmuInsertOpenMark = "「",
            DanmuInsertCloseMark = "」"
        };
        var viewModel = CreateConfiguredViewModel(settings);

        viewModel.InsertDanmuContent("弹幕内容");

        Assert.Equal("「弹幕内容」", viewModel.InputDraft);
    }

    [Fact]
    public void ClearInputDraftClearsState()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState())
        {
            InputDraft = "待发送"
        };

        viewModel.ClearInputDraft();

        Assert.Equal("", viewModel.InputDraft);
        Assert.Equal("", viewModel.State.InputDraft);
    }

    [Fact]
    public void ApplyLyricParsesInput()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState())
        {
            LyricInput = "[00:01]歌词"
        };
        viewModel.Configure(
            _ => null,
            (_, _) => null,
            () => new AppSettings(),
            () => new MarkSymbolGroup(),
            () => [],
            _ => [],
            new DanmuSendService(),
            new AvatarCacheService(
                new AppDirectoryService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))),
            () => Task.CompletedTask);

        viewModel.ApplyLyric();

        Assert.Single(viewModel.Lyrics);
        Assert.Equal("歌词", viewModel.Lyrics[0].Content);
    }

    [Fact]
    public void LyricPlaybackRateDefaultsToOne()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState());

        Assert.Equal(1.0, viewModel.LyricPlaybackRate);
        Assert.Equal(1.0, viewModel.State.LyricPlaybackRate);
    }

    [Fact]
    public void LyricSendModeDefaultsToBilingual()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState());

        Assert.Equal(LyricSendMode.Bilingual, viewModel.LyricSendMode);
        Assert.Equal(LyricSendMode.Bilingual, viewModel.State.LyricSendMode);
        Assert.Equal(
            ["原文 + 翻译", "仅原文", "仅翻译"],
            viewModel.LyricSendModeOptions.Select(option => option.DisplayName));
    }

    [Fact]
    public void LyricSendModeNormalizesUnknownValue()
    {
        var state = new LiveRoomTabState
        {
            LyricSendMode = (LyricSendMode)999
        };

        var viewModel = new LiveRoomTabViewModel(state);

        Assert.Equal(LyricSendMode.Bilingual, viewModel.LyricSendMode);
        Assert.Equal(LyricSendMode.Bilingual, state.LyricSendMode);
    }

    [Fact]
    public void LoadLyricKeepsOriginalAndTranslationTogether()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState());
        var item = new LyricLibraryItem
        {
            Title = "多语言歌曲",
            LyricText = "[00:01]原文",
            TranslatedLyricText = "[00:01]翻译"
        };

        viewModel.LoadLyric(item);

        var line = Assert.Single(viewModel.Lyrics);
        Assert.Equal("原文", line.Content);
        Assert.Equal("翻译", line.TranslatedContent);
        Assert.Equal("[00:01]原文", viewModel.LyricInput);
        Assert.Equal("[00:01]翻译", viewModel.TranslatedLyricInput);
    }

    [Fact]
    public async Task SendCurrentLyricIncludesOriginalAndTranslation()
    {
        var sendService = new FakeDanmuSendService();
        var viewModel = CreateConfiguredViewModel(new AppSettings(), sendService);
        viewModel.RoomId = "100";
        viewModel.LyricInput = "[00:01.00]原文";
        viewModel.TranslatedLyricInput = "[00:01.00]翻译";
        viewModel.ApplyLyric();

        await viewModel.SendCurrentLyricAsync();

        Assert.Equal(["【♪原文 / 翻译】"], sendService.SentMessages);
    }

    [Theory]
    [InlineData(LyricSendMode.OriginalOnly, "【♪原文】")]
    [InlineData(LyricSendMode.TranslationOnly, "【♪翻译】")]
    public async Task SendCurrentLyricUsesSelectedContentMode(LyricSendMode mode, string expected)
    {
        var sendService = new FakeDanmuSendService();
        var viewModel = CreateConfiguredViewModel(
            new AppSettings(),
            sendService,
            state: new()
            {
                LyricSendMode = mode
            });
        viewModel.RoomId = "100";
        viewModel.LyricInput = "[00:01.00]原文";
        viewModel.TranslatedLyricInput = "[00:01.00]翻译";
        viewModel.ApplyLyric();

        await viewModel.SendCurrentLyricAsync();

        Assert.Equal([expected], sendService.SentMessages);
        Assert.True(viewModel.Lyrics[0].IsSent);
    }

    [Fact]
    public async Task TranslationOnlySkipsLineWithoutTranslation()
    {
        var sendService = new FakeDanmuSendService();
        var viewModel = CreateConfiguredViewModel(
            new AppSettings(),
            sendService,
            state: new()
            {
                LyricSendMode = LyricSendMode.TranslationOnly
            });
        viewModel.RoomId = "100";
        viewModel.LyricInput = "[00:01.00]原文";
        viewModel.ApplyLyric();

        await viewModel.SendCurrentLyricAsync();

        Assert.Empty(sendService.SentMessages);
        Assert.False(viewModel.Lyrics[0].IsSent);
    }

    [Fact]
    public void LyricPlaybackRateNormalizesLegacyZero()
    {
        var state = new LiveRoomTabState
        {
            LyricPlaybackRate = 0
        };

        var viewModel = new LiveRoomTabViewModel(state);

        Assert.Equal(1.0, viewModel.LyricPlaybackRate);
        Assert.Equal(1.0, state.LyricPlaybackRate);
    }

    [Fact]
    public void LyricPlaybackRateClampsToSupportedRange()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState());

        viewModel.LyricPlaybackRate = 0.1;

        Assert.Equal(0.25, viewModel.LyricPlaybackRate);
        Assert.Equal(0.25, viewModel.State.LyricPlaybackRate);

        viewModel.LyricPlaybackRate = 9;

        Assert.Equal(3.0, viewModel.LyricPlaybackRate);
        Assert.Equal(3.0, viewModel.State.LyricPlaybackRate);
    }

    [Fact]
    public void LyricPlaybackRateButtonsAdjustByPointOneAndRespectBounds()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState());

        viewModel.DecreaseLyricPlaybackRate();

        Assert.Equal(0.9, viewModel.LyricPlaybackRate);

        viewModel.IncreaseLyricPlaybackRate();

        Assert.Equal(1.0, viewModel.LyricPlaybackRate);
        Assert.Equal(1.0, viewModel.State.LyricPlaybackRate);

        viewModel.LyricPlaybackRate = 0.25;
        viewModel.DecreaseLyricPlaybackRate();

        Assert.Equal(0.25, viewModel.LyricPlaybackRate);

        viewModel.IncreaseLyricPlaybackRate();

        Assert.Equal(0.35, viewModel.LyricPlaybackRate);

        viewModel.LyricPlaybackRate = 3.0;
        viewModel.IncreaseLyricPlaybackRate();

        Assert.Equal(3.0, viewModel.LyricPlaybackRate);
    }

    [Fact]
    public void LivePlayerVolumeClampsToSupportedRange()
    {
        var state = new LiveRoomTabState
        {
            LivePlayerVolumePercent = 150
        };
        var viewModel = new LiveRoomTabViewModel(state);

        Assert.Equal(100, viewModel.LivePlayerVolumePercent);
        Assert.Equal(100, state.LivePlayerVolumePercent);

        viewModel.LivePlayerVolumePercent = -10;

        Assert.Equal(0, viewModel.LivePlayerVolumePercent);
        Assert.Equal(0, state.LivePlayerVolumePercent);

        viewModel.LivePlayerVolumePercent = double.NaN;

        Assert.Equal(100, viewModel.LivePlayerVolumePercent);
        Assert.Equal(100, state.LivePlayerVolumePercent);
    }

    [Fact]
    public void ToggleLivePlayerMutedWritesState()
    {
        var state = new LiveRoomTabState();
        var viewModel = new LiveRoomTabViewModel(state);

        viewModel.ToggleLivePlayerMuted();

        Assert.True(viewModel.IsLivePlayerMuted);
        Assert.True(state.IsLivePlayerMuted);
        Assert.False(viewModel.IsLivePlayerAudible);
    }

    [Fact]
    public async Task ToggleLivePlayerStopsWhenVisible()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState())
        {
            IsLivePlayerVisible = true,
            LivePlayerUrl = "http://127.0.0.1/player"
        };

        await viewModel.ToggleLivePlayerAsync();

        Assert.False(viewModel.IsLivePlayerVisible);
        Assert.Equal("", viewModel.LivePlayerUrl);
    }

    [Fact]
    public void CalculateLyricPlaybackWaitScalesDelayByRate()
    {
        var wait = LiveRoomTabViewModel.CalculateLyricPlaybackWait(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(1),
            2.0);

        Assert.Equal(TimeSpan.FromSeconds(4), wait);
    }

    [Fact]
    public void ApplyLyricCalculatesDurationsAndResetsSentState()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState())
        {
            LyricInput = """
                         [00:01.00]第一句
                         [00:03.00]第二句
                         """
        };

        viewModel.ApplyLyric();
        viewModel.Lyrics[0].IsSent = true;
        viewModel.ApplyLyric();

        Assert.Equal(2, viewModel.Lyrics.Count);
        Assert.Equal(2, viewModel.Lyrics[0].DurationSeconds);
        Assert.Equal(3, viewModel.Lyrics[1].DurationSeconds);
        Assert.False(viewModel.Lyrics[0].IsSent);
        Assert.Equal(viewModel.Lyrics[0], viewModel.ActiveLyricLine);
    }

    [Fact]
    public void ClearLyricClearsInputTitleLinesAndPlaybackState()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState())
        {
            LyricTitle = "歌曲",
            LyricInput = "[00:01.00]歌词"
        };
        viewModel.ApplyLyric();
        viewModel.AdjustLyricPlaybackPosition(0.5);

        viewModel.ClearLyric();

        Assert.Equal("", viewModel.LyricTitle);
        Assert.Equal("", viewModel.LyricInput);
        Assert.Empty(viewModel.Lyrics);
        Assert.Null(viewModel.ActiveLyricLine);
        Assert.Equal(0, viewModel.LyricPlaybackPositionSeconds);
    }

    [Fact]
    public void RewindAndFastForwardLyricAdjustByHalfSecond()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState())
        {
            LyricInput = """
                         [00:01.00]第一句
                         [00:03.00]第二句
                         """
        };
        viewModel.ApplyLyric();

        viewModel.FastForwardLyric();
        viewModel.FastForwardLyric();
        viewModel.FastForwardLyric();

        Assert.Equal(2.5, viewModel.LyricPlaybackPositionSeconds);
        Assert.Equal(viewModel.Lyrics[0], viewModel.ActiveLyricLine);
        Assert.Equal(0.75, viewModel.Lyrics[0].Progress);

        viewModel.FastForwardLyric();

        Assert.Equal(3, viewModel.LyricPlaybackPositionSeconds);
        Assert.Equal(viewModel.Lyrics[1], viewModel.ActiveLyricLine);

        viewModel.RewindLyric();

        Assert.Equal(2.5, viewModel.LyricPlaybackPositionSeconds);
        Assert.Equal(viewModel.Lyrics[0], viewModel.ActiveLyricLine);
    }

    [Fact]
    public void SeekLyricLineStartMovesPlaybackToSelectedLine()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState())
        {
            LyricInput = """
                         [00:01.00]第一句
                         [00:03.00]第二句
                         [00:07.00]第三句
                         """
        };
        viewModel.ApplyLyric();

        viewModel.SeekLyricLineStart(viewModel.Lyrics[1]);

        Assert.Equal(3, viewModel.LyricPlaybackPositionSeconds);
        Assert.Equal(viewModel.Lyrics[1], viewModel.ActiveLyricLine);
        Assert.Equal(0, viewModel.Lyrics[1].Progress);
    }

    [Fact]
    public void SeekLyricLineStartMovesPlaybackToLastLineStart()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState())
        {
            LyricInput = """
                         [00:01.00]第一句
                         [00:03.00]第二句
                         [00:07.00]第三句
                         """
        };
        viewModel.ApplyLyric();

        viewModel.SeekLyricLineStart(viewModel.Lyrics[2]);

        Assert.Equal(7, viewModel.LyricPlaybackPositionSeconds);
        Assert.Equal(viewModel.Lyrics[2], viewModel.ActiveLyricLine);
        Assert.Equal(0, viewModel.Lyrics[2].Progress);
    }

    [Fact]
    public void SeekLyricLineStartIgnoresLineWithoutTimeline()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState())
        {
            LyricInput = """
                         [00:01.00]第一句
                         无时间轴
                         """
        };
        viewModel.ApplyLyric();
        viewModel.AdjustLyricPlaybackPosition(1);
        var untimedLine = viewModel.Lyrics.Single(line => line.TimeSeconds < 0);

        viewModel.SeekLyricLineStart(untimedLine);

        Assert.Equal(2, viewModel.LyricPlaybackPositionSeconds);
        Assert.Equal(viewModel.Lyrics[0], viewModel.ActiveLyricLine);
    }

    [Fact]
    public async Task SendCurrentLyricMarksLineSent()
    {
        var sendService = new FakeDanmuSendService();
        var viewModel = CreateConfiguredViewModel(new AppSettings(), sendService);
        viewModel.RoomId = "100";
        viewModel.LyricInput = "[00:01.00]歌词";
        viewModel.ApplyLyric();

        await viewModel.SendCurrentLyricAsync();

        Assert.True(viewModel.Lyrics[0].IsSent);
        Assert.Equal(["【♪歌词】"], sendService.SentMessages);
    }

    [Fact]
    public async Task SendDraftAppliesShieldReplacement()
    {
        var sendService = new FakeDanmuSendService();
        var settings = new AppSettings
        {
            ShieldReplacementRules =
            [
                new()
                {
                    SourceText = "猫",
                    ReplacementText = "狗"
                }
            ]
        };
        var viewModel = CreateConfiguredViewModel(settings, sendService);
        viewModel.RoomId = "100";
        viewModel.InputDraft = "猫来了";

        await viewModel.SendDraftAsync();

        Assert.Equal(["【狗来了】"], sendService.SentMessages);
    }

    [Fact]
    public void HideEmoticonDanmuFiltersOnlyProtocolEmoticonMessages()
    {
        var viewModel = CreateConfiguredViewModel(
            new()
            {
                HideEmoticonDanmu = true
            });

        viewModel.AddDanmu(new()
        {
            Uid = 1,
            UserName = "表情用户",
            Content = "[dog]",
            IsEmoticon = true
        });
        viewModel.AddDanmu(new()
        {
            Uid = 2,
            UserName = "文本用户",
            Content = "[公告]"
        });

        Assert.Equal("[公告]", viewModel.DanmuItems[0].Content);
    }

    [Fact]
    public void EnablingHideEmoticonDanmuKeepsExistingMessages()
    {
        var settings = new AppSettings();
        var viewModel = CreateConfiguredViewModel(settings);
        viewModel.AddDanmu(new()
        {
            Uid = 1,
            Content = "[first]",
            IsEmoticon = true
        });
        settings.HideEmoticonDanmu = true;
        viewModel.AddDanmu(new()
        {
            Uid = 1,
            Content = "[second]",
            IsEmoticon = true
        });
        Assert.Single(viewModel.DanmuItems);
        Assert.Equal("[first]", viewModel.DanmuItems[0].Content);
    }

    [Fact]
    public void HideEmoticonDanmuDoesNotFilterSuperChat()
    {
        var viewModel = CreateConfiguredViewModel(
            new()
            {
                HideEmoticonDanmu = true
            });

        viewModel.AddSuperChat(CreateSuperChatMessage("sc-emoticon"));

        Assert.Equal("sc-emoticon", viewModel.SuperChats[0].MessageId);
    }

    [Fact]
    public async Task SendDraftFailureKeepsInput()
    {
        var sendService = new FakeDanmuSendService
        {
            IsAccepted = false,
            ErrorMessage = "风控拒绝"
        };
        var viewModel = CreateConfiguredViewModel(new(), sendService);
        viewModel.RoomId = "100";
        viewModel.InputDraft = "待发送";

        await viewModel.SendDraftAsync();

        Assert.Equal("待发送", viewModel.InputDraft);
        Assert.Equal("发送失败：风控拒绝", viewModel.DraftSendStatus);
        Assert.True(viewModel.IsDraftSendRejected);
    }

    /// <summary>
    /// 没有明确接口错误码的失败保留草稿，但不显示失败描边。
    /// </summary>
    [Fact]
    public async Task SendDraftTransportFailureKeepsInputWithoutRejection()
    {
        var sendService = new FakeDanmuSendService
        {
            IsAccepted = false, FailureApiCode = null, ErrorMessage = "网络超时"
        };
        var viewModel = CreateConfiguredViewModel(new(), sendService);
        viewModel.InputDraft = "待发送";

        await viewModel.SendDraftAsync();

        Assert.False(viewModel.IsDraftSendRejected);
        Assert.Equal("待发送", viewModel.InputDraft);
    }

    /// <summary>
    /// 部分发送失败仅在存在明确接口拒绝时显示失败描边。
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(10030, true)]
    [InlineData(-101, true)]
    public async Task SendDraftPartialFailureOnlyHighlightsExplicitRejection(int? code, bool rejected)
    {
        var sendService = new FakeDanmuSendService
        {
            MessageParts = ["第一段", "第二段"], FailureApiCode = code
        };
        sendService.AcceptanceResults.Enqueue(true);
        sendService.AcceptanceResults.Enqueue(false);
        var viewModel = CreateConfiguredViewModel(new(), sendService);
        viewModel.InputDraft = "完整同传";

        await viewModel.SendDraftAsync();

        Assert.Equal(rejected, viewModel.IsDraftSendRejected);
        Assert.Equal("完整同传", viewModel.InputDraft);
    }

    /// <summary>
    /// 修改或清空输入会移除上次拒绝描边。
    /// </summary>
    [Theory]
    [InlineData("修正内容")]
    [InlineData("")]
    public async Task SendDraftEditingClearsRejection(string nextDraft)
    {
        var viewModel = CreateConfiguredViewModel(new(), new FakeDanmuSendService { IsAccepted = false });
        viewModel.InputDraft = "待发送";
        await viewModel.SendDraftAsync();
        Assert.True(viewModel.IsDraftSendRejected);

        viewModel.InputDraft = nextDraft;

        Assert.False(viewModel.IsDraftSendRejected);
    }

    /// <summary>
    /// 新一轮发送开始时立即移除上次拒绝描边。
    /// </summary>
    [Fact]
    public async Task SendDraftRetryClearsRejectionBeforeResponse()
    {
        var sendService = new BlockingDanmuSendService();
        var viewModel = CreateConfiguredViewModel(new(), sendService);
        viewModel.InputDraft = "重试内容";
        viewModel.IsDraftSendRejected = true;
        var sending = viewModel.SendDraftAsync();

        try
        {
            await sendService.FirstSendStarted;
            Assert.False(viewModel.IsDraftSendRejected);
        }
        finally
        {
            sendService.ReleaseFirstSend();
            await sending;
        }
    }

    /// <summary>
    /// 请求期间编辑过的草稿不会被旧响应清空或标红，即使后来恢复成原文。
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task SendDraftOldResponseDoesNotChangeEditedDraft(bool accepted, bool restoreOriginal)
    {
        var sendService = new BlockingDanmuSendService { IsAccepted = accepted };
        var viewModel = CreateConfiguredViewModel(new(), sendService);
        viewModel.InputDraft = "原始内容";
        var sending = viewModel.SendDraftAsync();

        try
        {
            await sendService.FirstSendStarted;
            viewModel.InputDraft = "新内容";

            if (restoreOriginal)
            {
                viewModel.InputDraft = "原始内容";
            }
        }
        finally
        {
            sendService.ReleaseFirstSend();
            await sending;
        }

        Assert.Equal(restoreOriginal ? "原始内容" : "新内容", viewModel.InputDraft);
        Assert.False(viewModel.IsDraftSendRejected);
    }

    /// <summary>
    /// 切换房间使旧请求失效，旧响应不会清空输入或触发描边。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendDraftRoomChangeInvalidatesOldResponse(bool accepted)
    {
        var sendService = new BlockingDanmuSendService { IsAccepted = accepted };
        var viewModel = CreateConfiguredViewModel(new(), sendService);
        viewModel.RoomId = "100";
        viewModel.InputDraft = "房间内容";
        var sending = viewModel.SendDraftAsync();

        try
        {
            await sendService.FirstSendStarted;
            viewModel.RoomId = "200";
        }
        finally
        {
            sendService.ReleaseFirstSend();
            await sending;
        }

        Assert.Equal("房间内容", viewModel.InputDraft);
        Assert.False(viewModel.IsDraftSendRejected);
    }

    /// <summary>
    /// 两次请求逆序完成时，旧拒绝不能覆盖新请求的成功状态。
    /// </summary>
    [Fact]
    public async Task SendDraftOlderRejectionCannotOverwriteNewerSuccess()
    {
        var sendService = new BlockingDanmuSendService { IsAccepted = false };
        var viewModel = CreateConfiguredViewModel(new(), sendService);
        viewModel.InputDraft = "待发送";
        var first = viewModel.SendDraftAsync();

        try
        {
            await sendService.FirstSendStarted;
            sendService.IsAccepted = true;
            await viewModel.SendDraftAsync();
        }
        finally
        {
            sendService.ReleaseFirstSend();
            await first;
        }

        Assert.False(viewModel.IsDraftSendRejected);
        Assert.Equal("", viewModel.InputDraft);
        Assert.Equal("接口已接受，无法确认", viewModel.DraftSendStatus);
    }

    [Fact]
    public async Task SendDraftApiAcceptanceClearsInputWithoutListening()
    {
        var sendService = new FakeDanmuSendService();
        var account = new BilibiliAccount
        {
            Cookie = "DedeUserID=42"
        };
        var viewModel = CreateConfiguredViewModel(new(), sendService, resolveAccount: _ => account);
        viewModel.RoomId = "100";
        viewModel.InputDraft = "待发送";

        await viewModel.SendDraftAsync();

        Assert.Equal("", viewModel.InputDraft);
        Assert.Equal("接口已接受，无法确认", viewModel.DraftSendStatus);
        Assert.False(viewModel.IsDraftSendRejected);
        Assert.Equal("接口已接受，无法确认", Assert.Single(sendService.Records).Status);
    }

    [Fact]
    public async Task SendDraftPartialFailureKeepsInputAndReportsAcceptedParts()
    {
        var sendService = new FakeDanmuSendService
        {
            MessageParts = ["第一段", "第二段"],
            ErrorMessage = "第二段失败"
        };
        sendService.AcceptanceResults.Enqueue(true);
        sendService.AcceptanceResults.Enqueue(false);
        var viewModel = CreateConfiguredViewModel(new(), sendService);
        viewModel.RoomId = "100";
        viewModel.InputDraft = "完整同传";

        await viewModel.SendDraftAsync();

        Assert.Equal("完整同传", viewModel.InputDraft);
        Assert.Equal("部分发送失败：1/2 段接口已接受", viewModel.DraftSendStatus);
        Assert.True(viewModel.IsDraftSendRejected);
        Assert.Equal("接口已接受，无法确认", sendService.Records[0].Status);
        Assert.Equal("第二段失败", sendService.Records[1].Status);
    }

    [Fact]
    public async Task SendDraftEchoWithMatchingUidAndContentConfirmsDisplay()
    {
        var sendService = new FakeDanmuSendService();
        var socketFactory = new FakeLiveDanmuSocketFactory();
        socketFactory.Socket.KeepRunning = true;
        var account = new BilibiliAccount
        {
            Cookie = "DedeUserID=42"
        };
        var viewModel = CreateConfiguredViewModel(
            new(),
            sendService,
            resolveAccount: _ => account,
            liveDanmuSocketFactory: socketFactory);
        viewModel.RoomId = "100";
        viewModel.InputDraft = "待发送";
        viewModel.StartListening();

        await viewModel.SendDraftAsync();
        socketFactory.Socket.EmitDanmu(new()
        {
            Uid = 42,
            UserName = "自己",
            Content = "【待发送】"
        });
        await WaitForAsync(() => viewModel.DraftSendStatus == "已在弹幕流确认");

        Assert.Equal("已在弹幕流确认", Assert.Single(sendService.Records).Status);
        Assert.False(viewModel.IsDraftSendRejected);
        viewModel.StopListening();
    }

    [Fact]
    public async Task SendCurrentLyricAppliesShieldReplacementWhenEnabled()
    {
        var sendService = new FakeDanmuSendService();
        var settings = new AppSettings
        {
            ApplyShieldReplacementToLyrics = true,
            ShieldReplacementRules =
            [
                new()
                {
                    SourceText = "猫",
                    ReplacementText = "狗"
                }
            ]
        };
        var viewModel = CreateConfiguredViewModel(settings, sendService);
        viewModel.RoomId = "100";
        viewModel.LyricInput = "[00:01.00]猫来了";
        viewModel.ApplyLyric();

        await viewModel.SendCurrentLyricAsync();

        Assert.Equal(["【♪狗来了】"], sendService.SentMessages);
    }

    [Fact]
    public async Task SendCurrentLyricKeepsOriginalWhenLyricReplacementDisabled()
    {
        var sendService = new FakeDanmuSendService();
        var settings = new AppSettings
        {
            ApplyShieldReplacementToLyrics = false,
            ShieldReplacementRules =
            [
                new()
                {
                    SourceText = "猫",
                    ReplacementText = "狗"
                }
            ]
        };
        var viewModel = CreateConfiguredViewModel(settings, sendService);
        viewModel.RoomId = "100";
        viewModel.LyricInput = "[00:01.00]猫来了";
        viewModel.ApplyLyric();

        await viewModel.SendCurrentLyricAsync();

        Assert.Equal(["【♪猫来了】"], sendService.SentMessages);
    }

    [Fact]
    public async Task AutoLyricSkipsAlreadySentLineByDefault()
    {
        var sendService = new FakeDanmuSendService();
        var viewModel = CreateConfiguredViewModel(new AppSettings(), sendService);
        viewModel.RoomId = "100";
        viewModel.LyricInput = "[00:01.00]歌词";
        viewModel.ApplyLyric();
        viewModel.Lyrics[0].IsSent = true;

        var autoTask = viewModel.ToggleAutoLyricAsync();
        await Task.Delay(100);
        viewModel.StopAutoLyric();
        await autoTask;

        Assert.Empty(sendService.SentMessages);
    }

    [Fact]
    public async Task ToggleAutoLyricCommandRemainsEnabledWhilePlaying()
    {
        var viewModel = CreateConfiguredViewModel(new AppSettings(), new FakeDanmuSendService());
        viewModel.RoomId = "100";
        viewModel.LyricInput = "[00:05.00]歌词";
        viewModel.ApplyLyric();
        viewModel.Lyrics[0].IsSent = true;

        viewModel.ToggleAutoLyricCommand.Execute(null);
        await WaitForAsync(() => viewModel.IsLyricAutoSending);

        Assert.True(viewModel.ToggleAutoLyricCommand.CanExecute(null));

        viewModel.ToggleAutoLyricCommand.Execute(null);
        await WaitForAsync(() => !viewModel.IsLyricAutoSending);
    }

    [Fact]
    public async Task OldAutoLyricSessionCannotStopRapidlyRestartedSession()
    {
        var sendService = new BlockingDanmuSendService();
        var viewModel = CreateConfiguredViewModel(new AppSettings(), sendService);
        viewModel.RoomId = "100";
        viewModel.LyricInput = "[00:05.00]歌词";
        viewModel.ApplyLyric();

        var firstTask = viewModel.StartAutoLyricAsync();
        await sendService.FirstSendStarted.WaitAsync(TimeSpan.FromSeconds(1));

        viewModel.StopAutoLyric();
        var secondTask = viewModel.StartAutoLyricAsync();
        await WaitForAsync(() => viewModel.IsLyricAutoSending);
        sendService.ReleaseFirstSend();
        await firstTask;

        Assert.True(viewModel.IsLyricAutoSending);

        viewModel.StopAutoLyric();
        await secondTask;
    }

    [Fact]
    public async Task StartAutoLyricStartsWithoutTogglingExistingSessionOff()
    {
        var sendService = new FakeDanmuSendService();
        var viewModel = CreateConfiguredViewModel(new AppSettings(), sendService);
        viewModel.RoomId = "100";
        viewModel.LyricInput = "[00:05.00]歌词";
        viewModel.ApplyLyric();
        viewModel.Lyrics[0].IsSent = true;

        var autoTask = viewModel.StartAutoLyricAsync();
        await Task.Delay(100);

        Assert.True(viewModel.IsLyricAutoSending);

        await viewModel.StartAutoLyricAsync();

        Assert.True(viewModel.IsLyricAutoSending);

        viewModel.StopAutoLyric();
        await autoTask;

        Assert.Empty(sendService.SentMessages);
    }

    [Fact]
    public async Task AutoLyricCanRepeatAlreadySentLineWhenEnabled()
    {
        var sendService = new FakeDanmuSendService();
        var state = new LiveRoomTabState
        {
            PreventRepeatedLyricSend = false,
            LyricText = "[00:01.00]歌词"
        };
        var viewModel = new LiveRoomTabViewModel(state);
        viewModel.Configure(
            _ => null,
            (_, _) => null,
            () => new AppSettings(),
            () => new MarkSymbolGroup(),
            () => [],
            _ => [],
            sendService,
            new AvatarCacheService(
                new AppDirectoryService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))),
            () => Task.CompletedTask);
        viewModel.RoomId = "100";
        viewModel.ApplyLyric();
        viewModel.Lyrics[0].IsSent = true;

        var autoTask = viewModel.ToggleAutoLyricAsync();
        await Task.Delay(100);
        viewModel.StopAutoLyric();
        await autoTask;

        Assert.Equal(["【♪歌词】"], sendService.SentMessages);
        Assert.False(state.PreventRepeatedLyricSend);
    }

    [Fact]
    public void CurrentLyricTitleTextFallsBackWhenEmpty()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState());

        Assert.Equal("未选择歌词", viewModel.CurrentLyricTitleText);

        viewModel.LyricTitle = "歌曲";

        Assert.Equal("歌曲", viewModel.CurrentLyricTitleText);
    }

    [Fact]
    public void ForwardRuleIgnoresNullSourceRoomKey()
    {
        var state = new DanmuForwardRuleState
        {
            SourceWorkspaceId = "workspace",
            SourceRoomStateId = "room"
        };
        var changed = false;
        var viewModel = new DanmuForwardRuleViewModel(state, _ => changed = true);

        viewModel.SourceRoomKey = null;

        Assert.False(changed);
        Assert.Equal("workspace", state.SourceWorkspaceId);
        Assert.Equal("room", state.SourceRoomStateId);
    }

    [Fact]
    public void ForwardRuleUpdatesSourceRoomKey()
    {
        var state = new DanmuForwardRuleState
        {
            SourceWorkspaceId = "workspace",
            SourceRoomStateId = "room"
        };
        var changed = false;
        var viewModel = new DanmuForwardRuleViewModel(state, _ => changed = true);

        viewModel.SourceRoomKey = "next-workspace|next-room";

        Assert.True(changed);
        Assert.Equal("next-workspace", state.SourceWorkspaceId);
        Assert.Equal("next-room", state.SourceRoomStateId);
    }

    [Fact]
    public void RefreshForwardSourceRoomsKeepsExistingItemsWhenSourceKeysDoNotChange()
    {
        var sources = new[]
        {
            new ForwardSourceRoomOption
            {
                WorkspaceId = "workspace",
                WorkspaceName = "工作区",
                RoomStateId = "room",
                RoomId = "100",
                RoomName = "直播间"
            }
        };
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState());
        viewModel.Configure(
            _ => null,
            (_, _) => null,
            () => new AppSettings(),
            () => new MarkSymbolGroup(),
            () => [],
            _ => sources,
            new DanmuSendService(),
            new AvatarCacheService(
                new AppDirectoryService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))),
            () => Task.CompletedTask);

        viewModel.RefreshForwardSourceRooms();
        var firstItem = viewModel.ForwardSourceRooms[0];

        viewModel.RefreshForwardSourceRooms();

        Assert.Same(firstItem, viewModel.ForwardSourceRooms[0]);
    }

    [Fact]
    public async Task ForwardRuleUsesSpecifiedAccountWhenConfigured()
    {
        var sourceAccount = new BilibiliAccount
        {
            Id = "source-account",
            Cookie = "source-cookie"
        };
        var targetAccount = new BilibiliAccount
        {
            Id = "target-account",
            Cookie = "target-cookie"
        };
        var specifiedAccount = new BilibiliAccount
        {
            Id = "specified-account",
            Cookie = "specified-cookie"
        };
        var source = CreateForwardSourceRoomOption();
        var sendService = new FakeDanmuSendService();
        var socketFactory = new FakeLiveDanmuSocketFactory();
        socketFactory.Socket.NextMessage = new()
        {
            RoomId = source.RoomId,
            Uid = 42,
            UserName = "发送者",
            Content = "转发内容"
        };
        var state = CreateForwardTargetState(source, specifiedAccount.Id);

        var viewModel = CreateConfiguredViewModel(
            new(),
            sendService,
            resolveAccount: _ => targetAccount,
            resolveAccountByRoom: (_, _) => sourceAccount,
            resolveAccountById: accountId => accountId == specifiedAccount.Id ? specifiedAccount : null,
            getForwardSourceRooms: _ => [source],
            liveDanmuSocketFactory: socketFactory,
            state: state);

        viewModel.RestartForwarding();
        await WaitForAsync(() => sendService.SentAccounts.Count > 0);

        Assert.Same(specifiedAccount, sendService.SentAccounts[0]);
        Assert.Equal(["【转发内容】"], sendService.SentMessages);
        Assert.Equal(100L, socketFactory.Requests[0].RoomId);
        Assert.Equal("source-cookie", socketFactory.Requests[0].Cookie);
    }

    [Fact]
    public async Task ForwardRuleFallsBackToTargetRoomAccountWhenAccountNotSpecified()
    {
        var sourceAccount = new BilibiliAccount
        {
            Id = "source-account",
            Cookie = "source-cookie"
        };
        var targetAccount = new BilibiliAccount
        {
            Id = "target-account",
            Cookie = "target-cookie"
        };
        var source = CreateForwardSourceRoomOption();
        var sendService = new FakeDanmuSendService();
        var socketFactory = new FakeLiveDanmuSocketFactory();
        socketFactory.Socket.NextMessage = new()
        {
            RoomId = source.RoomId,
            Uid = 42,
            UserName = "发送者",
            Content = "转发内容"
        };
        var state = CreateForwardTargetState(source, "");

        var viewModel = CreateConfiguredViewModel(
            new(),
            sendService,
            resolveAccount: _ => targetAccount,
            resolveAccountByRoom: (_, _) => sourceAccount,
            resolveAccountById: _ => throw new InvalidOperationException("未指定账号时不应解析规则账号"),
            getForwardSourceRooms: _ => [source],
            liveDanmuSocketFactory: socketFactory,
            state: state);

        viewModel.RestartForwarding();
        await WaitForAsync(() => sendService.SentAccounts.Count > 0);

        Assert.Same(targetAccount, sendService.SentAccounts[0]);
        Assert.Equal(["【转发内容】"], sendService.SentMessages);
    }

    [Fact]
    public async Task ForwardRuleShowsMissingStatusWhenSpecifiedAccountDoesNotExist()
    {
        var sourceAccount = new BilibiliAccount
        {
            Id = "source-account",
            Cookie = "source-cookie"
        };
        var targetAccount = new BilibiliAccount
        {
            Id = "target-account",
            Cookie = "target-cookie"
        };
        var source = CreateForwardSourceRoomOption();
        var socketFactory = new FakeLiveDanmuSocketFactory();
        var state = CreateForwardTargetState(source, "missing-account");

        var viewModel = CreateConfiguredViewModel(
            new(),
            resolveAccount: _ => targetAccount,
            resolveAccountByRoom: (_, _) => sourceAccount,
            resolveAccountById: _ => null,
            getForwardSourceRooms: _ => [source],
            liveDanmuSocketFactory: socketFactory,
            state: state);

        viewModel.RestartForwarding();
        await WaitForAsync(() => viewModel.ForwardRules[0].StatusText == "指定账号不存在");

        Assert.Equal("指定账号不存在", viewModel.ForwardRules[0].StatusText);
        Assert.Empty(socketFactory.Requests);
    }

    [Fact]
    public async Task ForwardRuleShowsCookieStatusWhenSpecifiedAccountHasNoCookie()
    {
        var sourceAccount = new BilibiliAccount
        {
            Id = "source-account",
            Cookie = "source-cookie"
        };
        var targetAccount = new BilibiliAccount
        {
            Id = "target-account",
            Cookie = "target-cookie"
        };
        var specifiedAccount = new BilibiliAccount
        {
            Id = "specified-account",
            Cookie = ""
        };
        var source = CreateForwardSourceRoomOption();
        var socketFactory = new FakeLiveDanmuSocketFactory();
        var state = CreateForwardTargetState(source, specifiedAccount.Id);

        var viewModel = CreateConfiguredViewModel(
            new(),
            resolveAccount: _ => targetAccount,
            resolveAccountByRoom: (_, _) => sourceAccount,
            resolveAccountById: _ => specifiedAccount,
            getForwardSourceRooms: _ => [source],
            liveDanmuSocketFactory: socketFactory,
            state: state);

        viewModel.RestartForwarding();
        await WaitForAsync(() => viewModel.ForwardRules[0].StatusText == "指定账号缺少 Cookie");

        Assert.Equal("指定账号缺少 Cookie", viewModel.ForwardRules[0].StatusText);
        Assert.Empty(socketFactory.Requests);
    }

    [Fact]
    public void RoomInfoPropertiesWriteState()
    {
        var state = new LiveRoomTabState
        {
            RoomId = "100",
            RoomName = "旧直播间",
            OwnerUid = "1"
        };
        var viewModel = new LiveRoomTabViewModel(state)
        {
            RoomId = "200",
            RoomName = "新直播间",
            OwnerUid = "2"
        };

        Assert.Equal("200", state.RoomId);
        Assert.Equal("新直播间", state.RoomName);
        Assert.Equal("2", state.OwnerUid);
    }

    [Fact]
    public void AvatarPlaceholderUsesRoomNameInitialAndStableRoomColor()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState
        {
            RoomId = "12345",
            RoomName = "测试直播间"
        });

        var firstColor = LiveRoomTabViewModel.CreateAvatarPlaceholderColor("12345");
        var secondColor = LiveRoomTabViewModel.CreateAvatarPlaceholderColor("12345");
        var otherColor = LiveRoomTabViewModel.CreateAvatarPlaceholderColor("54321");

        Assert.Equal("测", viewModel.AvatarPlaceholderText);
        Assert.Equal(firstColor, secondColor);
        Assert.NotEqual(firstColor, otherColor);
    }

    [Fact]
    public void AvatarPlaceholderFallsBackToRoomIdFirstCharacter()
    {
        var viewModel = new LiveRoomTabViewModel(new LiveRoomTabState
        {
            RoomId = "67890",
            RoomName = ""
        });

        Assert.Equal("6", viewModel.AvatarPlaceholderText);
    }

    [Fact]
    public async Task ChaseLiveRevokesOldPlayerTokenAndCreatesNewUrl()
    {
        var liveStreamService = new FakeBilibiliLiveStreamService();
        var livePlayerService = new FakeLivePlayerService();
        var viewModel = CreateConfiguredViewModel(
            new AppSettings(),
            liveStreamService: liveStreamService,
            livePlayerService: livePlayerService);

        await viewModel.PlayLiveAsync();
        var firstUrl = viewModel.LivePlayerUrl;

        await viewModel.ChaseLiveAsync();

        Assert.Equal("http://127.0.0.1/player?token=token-2", viewModel.LivePlayerUrl);
        Assert.NotEqual(firstUrl, viewModel.LivePlayerUrl);
        Assert.Equal(["token-1"], livePlayerService.RevokedTokens);
    }

    [Fact]
    public async Task PlayLiveAttemptsListeningWhenAutoStartEnabled()
    {
        var viewModel = CreateConfiguredViewModel(
            new()
            {
                AutoStartListeningWithLivePlayer = true
            },
            liveStreamService: new FakeBilibiliLiveStreamService(),
            livePlayerService: new FakeLivePlayerService());
        viewModel.RoomId = "100";

        await viewModel.PlayLiveAsync();

        Assert.True(viewModel.IsLivePlayerVisible);
        Assert.Equal("缺少账号或房间", viewModel.ConnectionStatus);
    }

    [Fact]
    public async Task PlayLiveDoesNotStartListeningWhenAutoStartDisabled()
    {
        var viewModel = CreateConfiguredViewModel(
            new()
            {
                AutoStartListeningWithLivePlayer = false
            },
            liveStreamService: new FakeBilibiliLiveStreamService(),
            livePlayerService: new FakeLivePlayerService());
        viewModel.RoomId = "100";

        await viewModel.PlayLiveAsync();

        Assert.True(viewModel.IsLivePlayerVisible);
        Assert.Equal("未连接", viewModel.ConnectionStatus);
    }

    private static ForwardSourceRoomOption CreateForwardSourceRoomOption()
    {
        return new()
        {
            WorkspaceId = "source-workspace",
            WorkspaceName = "来源工作区",
            RoomStateId = "source-room",
            RoomId = "100",
            RoomName = "来源直播间"
        };
    }

    private static BilibiliSuperChatMessage CreateSuperChatMessage(string messageId)
    {
        return new()
        {
            MessageId = messageId,
            RoomId = "100",
            UserName = "用户",
            Price = 30,
            PriceText = "￥30",
            BorderColor = "#3171D2",
            Content = "SC内容",
            Timestamp = 123456
        };
    }

    private static LiveRoomTabState CreateForwardTargetState(
        ForwardSourceRoomOption source,
        string accountOverrideId)
    {
        return new()
        {
            RoomId = "200",
            ForwardRules =
            [
                new()
                {
                    IsEnabled = true,
                    SourceWorkspaceId = source.WorkspaceId,
                    SourceRoomStateId = source.RoomStateId,
                    SenderUid = "42",
                    AccountOverrideId = accountOverrideId
                }
            ]
        };
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        for (var index = 0; index < 50; index++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate());
    }

    private static LiveRoomTabViewModel CreateConfiguredViewModel(
        AppSettings settings,
        IDanmuSendService? sendService = null,
        IBilibiliLiveStreamService? liveStreamService = null,
        ILivePlayerService? livePlayerService = null,
        Func<LiveRoomTabViewModel, BilibiliAccount?>? resolveAccount = null,
        Func<string, string, BilibiliAccount?>? resolveAccountByRoom = null,
        Func<string, BilibiliAccount?>? resolveAccountById = null,
        Func<LiveRoomTabViewModel, IEnumerable<ForwardSourceRoomOption>>? getForwardSourceRooms = null,
        ILiveDanmuSocketFactory? liveDanmuSocketFactory = null,
        LiveRoomTabState? state = null)
    {
        var viewModel = new LiveRoomTabViewModel(state ?? new LiveRoomTabState());
        viewModel.Configure(
            resolveAccount ?? (_ => null),
            resolveAccountByRoom ?? ((_, _) => null),
            () => settings,
            () => MarkSymbolService.CreateDefaultGroup(settings),
            () => [],
            getForwardSourceRooms ?? (_ => []),
            sendService ?? new DanmuSendService(),
            new AvatarCacheService(
                new AppDirectoryService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))),
            () => Task.CompletedTask,
            liveStreamService,
            livePlayerService,
            resolveAccountById,
            liveDanmuSocketFactory);

        return viewModel;
    }
}
