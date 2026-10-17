using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证房间状态与离线预览原位切换语言时保留业务状态及生命周期。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class RoomLocalizationTests
{
    /// <summary>
    /// 切换语言刷新当前状态、选项和应用生成的礼物正文，同时保留用户草稿和规则。
    /// </summary>
    [AvaloniaFact]
    public void RoomSwitchPreservesDraftsRulesAndOptionInstances()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var state = new LiveRoomTabState
        {
            RoomId = "100",
            RoomName = "用户自定房间",
            InputDraft = "未完成 [草稿",
            LyricText = "[00:01]原文歌词",
            TranslatedLyricText = "[00:01]Translated lyric",
            LyricSendMode = LyricSendMode.TranslationOnly,
            ForwardRules = [new() { SenderUid = "42", ContentPattern = "用户正则(未完成" }]
        };
        using var room = new LiveRoomTabViewModel(state);
        var options = room.LyricSendModeOptions.ToArray();
        var rule = Assert.Single(room.ForwardRules);
        rule.SenderUid = "invalid-uid";
        room.StartListening();
        room.AddGift(new BilibiliGiftMessage
        {
            Uid = 42,
            UserName = "真实用户",
            GiftName = "用户礼物名",
            Quantity = 2,
            UnitPrice = 1000,
            RoomId = "100"
        });
        var gift = Assert.Single(room.DanmuItems);
        var originalConnectionStatus = room.ConnectionStatus;
        var originalRuleStatus = rule.StatusText;
        var originalUidStatus = rule.SenderUserNameDisplayText;
        var originalGiftContent = gift.Content;
        var originalModeText = options[0].DisplayName;

        Localization.SetLanguage("en-US");

        Assert.NotEqual(originalConnectionStatus, room.ConnectionStatus);
        Assert.NotEqual(originalRuleStatus, rule.StatusText);
        Assert.NotEqual(originalUidStatus, rule.SenderUserNameDisplayText);
        Assert.NotEqual(originalGiftContent, gift.Content);
        Assert.NotEqual(originalModeText, options[0].DisplayName);
        Assert.Contains("用户礼物名", gift.Content);
        Assert.Equal("真实用户", gift.UserName);
        Assert.Equal("未完成 [草稿", room.InputDraft);
        Assert.Equal("用户自定房间", room.RoomName);
        Assert.Equal("[00:01]原文歌词", room.LyricInput);
        Assert.Equal("[00:01]Translated lyric", room.TranslatedLyricInput);
        Assert.Equal(LyricSendMode.TranslationOnly, room.LyricSendMode);
        Assert.Same(rule, Assert.Single(room.ForwardRules));
        Assert.Equal("用户正则(未完成", rule.ContentPattern);
        Assert.All(options, option => Assert.Contains(room.LyricSendModeOptions, current => ReferenceEquals(current, option)));

        Localization.SetLanguage("zh-CN");
        Assert.Equal(originalConnectionStatus, room.ConnectionStatus);
        Assert.Equal(originalGiftContent, gift.Content);
    }

    /// <summary>
    /// 发送失败后切换语言会同时刷新外层提示、服务生成的失败详情和历史记录。
    /// </summary>
    [AvaloniaFact]
    public async Task FailedDraftFeedbackRendersServiceFailureInCurrentLanguage()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        using var room = new LiveRoomTabViewModel(new() { RoomId = "100", InputDraft = "草稿原文" });
        var settings = new AppSettings();
        var service = new DanmuSendService();
        room.Configure(_ => null, (_, _) => null, () => settings,
            () => MarkSymbolService.CreateDefaultGroup(settings), () => [], _ => [],
            service, new FakeRoomAvatarService(), () => Task.CompletedTask);
        await room.SendDraftAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(room.IsDraftSendFailed);
        var previousError = room.DraftSendError;
        var record = Assert.Single(room.TranslateHistoryItems);

        Localization.SetLanguage("en-US");

        Assert.NotEqual(previousError, room.DraftSendError);
        Assert.Equal(Localization.Format("Room.Draft.Failed", Localization.Get("Services.Send.NoAccount")), room.DraftSendError);
        Assert.Equal(Localization.Get("Services.Send.NoAccount"), record.Status);
        Assert.True(room.IsDraftSendFailed);
        Assert.Equal("草稿原文", room.InputDraft);
    }

    /// <summary>
    /// 真实配置解析错误跨过服务、发送记录和房间反馈后，原因仍按当前语言完整刷新。
    /// </summary>
    [AvaloniaFact]
    public async Task ConfigurationAndSendingFailuresKeepApplicationExceptionSemantics()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var failure = Assert.Throws<InvalidOperationException>(() => BilibiliDanmuConfigSelector.Select(null, null));
        var originalExceptionMessage = failure.Message;
        var configurations = new FakeBilibiliDanmuConfigService { Exception = failure };
        var sends = new DanmuSendService(danmuConfigService: configurations);
        var account = new BilibiliAccount { Cookie = "DedeUserID=42; bili_jct=test; SESSDATA=test" };
        var settings = new AppSettings();
        using var room = new LiveRoomTabViewModel(new() { RoomId = "100", InputDraft = "用户发送草稿" });
        room.Configure(_ => account, (_, _) => account, () => settings,
            () => MarkSymbolService.CreateDefaultGroup(settings), () => [], _ => [],
            sends, new FakeRoomAvatarService(), () => Task.CompletedTask, danmuConfigService: configurations);
        await room.ActivateDanmuConfigurationAsync();
        await room.SendDraftAsync();
        Dispatcher.UIThread.RunJobs();
        var originalConfigurationStatus = room.DanmuConfigurationStatus;
        var originalSendError = room.DraftSendError;
        var record = Assert.Single(room.TranslateHistoryItems);
        Assert.True(room.IsDraftSendFailed);
        Assert.Equal(2, configurations.GetRequests.Count);

        Localization.SetLanguage("en-US");

        var currentReason = LocalizedExceptionText.Get(failure);
        Assert.NotEqual(originalExceptionMessage, currentReason);
        Assert.Equal(originalExceptionMessage, failure.Message);
        Assert.NotEqual(originalConfigurationStatus, room.DanmuConfigurationStatus);
        Assert.NotEqual(originalSendError, room.DraftSendError);
        Assert.Equal(Localization.Format("Room.ConfigurationLoadFailed", currentReason), room.DanmuConfigurationStatus);
        Assert.Equal(Localization.Format("Room.Draft.Failed", currentReason), room.DraftSendError);
        Assert.Equal(currentReason, record.Status);
        Assert.True(room.IsDraftSendFailed);
        Assert.Equal("用户发送草稿", room.InputDraft);
        Assert.Equal(2, configurations.GetRequests.Count);
    }

    /// <summary>
    /// 搜索结果在原对象上刷新房间状态说明，保留主播昵称和直播标题。
    /// </summary>
    [AvaloniaFact]
    public void SearchResultRefreshPreservesServerValues()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var result = new LiveRoomSearchResult { RoomId = "100", OwnerName = "主播原名", Title = "直播原标题", IsLive = true };
        var previousDetail = result.DetailLine;
        var changes = new List<string?>();
        result.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Localization.SetLanguage("en-US");
        result.RefreshLocalization();

        Assert.NotEqual(previousDetail, result.DetailLine);
        Assert.Contains(nameof(LiveRoomSearchResult.DetailLine), changes);
        Assert.Equal("主播原名", result.OwnerName);
        Assert.Equal("直播原标题", result.Title);
        Assert.Equal("100", result.RoomId);
        Assert.True(result.IsLive);
    }

    /// <summary>
    /// 正在监听和播放的房间切换语言时不重新取流、不释放会话、不重建监听。
    /// </summary>
    [AvaloniaFact]
    public async Task LanguageSwitchPreservesActiveListeningAndPlayerSessions()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        using var room = new LiveRoomTabViewModel(new() { RoomId = "100" });
        var account = new BilibiliAccount { Cookie = "DedeUserID=42; bili_jct=test; SESSDATA=test" };
        var settings = new AppSettings();
        var sockets = new FakeLiveDanmuSocketFactory();
        sockets.Socket.KeepRunning = true;
        var streams = new FakeBilibiliLiveStreamService();
        var players = new FakeLivePlayerService();
        room.Configure(_ => account, (_, _) => account, () => settings,
            () => MarkSymbolService.CreateDefaultGroup(settings), () => [], _ => [],
            new FakeDanmuSendService(), new FakeRoomAvatarService(), () => Task.CompletedTask,
            streams, players, liveDanmuSocketFactory: sockets);

        try
        {
            room.StartListening();
            await sockets.Socket.StartedTask.WaitAsync(TimeSpan.FromSeconds(5));
            await room.PlayLiveAsync();
            var playerUrl = room.LivePlayerUrl;
            var quality = room.SelectedLiveQuality;
            sockets.Socket.EmitDisconnected();
            Dispatcher.UIThread.RunJobs();
            var previousStatus = room.ConnectionStatus;

            Localization.SetLanguage("en-US");

            Assert.NotEqual(previousStatus, room.ConnectionStatus);
            Assert.True(room.IsListening);
            Assert.True(room.IsLivePlayerVisible);
            Assert.Equal(playerUrl, room.LivePlayerUrl);
            Assert.Same(quality, room.SelectedLiveQuality);
            Assert.Single(sockets.Requests);
            Assert.Single(streams.RequestedQualities);
            Assert.Empty(players.RevokedTokens);
            Assert.False(sockets.Socket.DisposedTask.IsCompleted);
        }
        finally
        {
            room.StopListening();
            await sockets.Socket.DisposedTask.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 预览语言切换保留样本、选中档位、自定颜色和用户输入。
    /// </summary>
    [AvaloniaFact]
    public void PreviewSwitchPreservesSamplesSelectionsAndUserInput()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        using var preview = new DanmuStylePreviewViewModel();
        preview.UserName = "自定预览用户";
        preview.Content = "用户输入正文 [未完成";
        preview.DanmuStatus = "自定状态";
        preview.SelectedSuperChatPreset = preview.SuperChatPresets[2];
        preview.SelectedGuardPreset = preview.GuardPresets[1];
        preview.SelectedMedalGuardPreset = preview.MedalGuardPresets[3];
        preview.SuperChatColorText = "#123456";
        preview.AddDanmu();
        var created = preview.DanmuItems[0];
        var danmuItems = preview.DanmuItems.ToArray();
        var superChats = preview.SuperChats.ToArray();
        var selectedSuperChat = preview.SelectedSuperChatPreset;
        var selectedGuard = preview.SelectedGuardPreset;
        var selectedMedal = preview.SelectedMedalGuardPreset;
        var originalBadge = superChats[0].BadgeText;
        var originalSampleContent = danmuItems[1].Content;

        Localization.SetLanguage("en-US");

        Assert.NotEqual(originalBadge, superChats[0].BadgeText);
        Assert.NotEqual(originalSampleContent, danmuItems[1].Content);
        Assert.Equal("自定预览用户", preview.UserName);
        Assert.Equal("用户输入正文 [未完成", preview.Content);
        Assert.Equal("自定状态", preview.DanmuStatus);
        Assert.Equal("用户输入正文 [未完成", created.Content);
        Assert.Equal("自定状态", created.Status);
        Assert.Equal("#123456", preview.SuperChatColorText);
        Assert.Same(selectedSuperChat, preview.SelectedSuperChatPreset);
        Assert.Same(selectedGuard, preview.SelectedGuardPreset);
        Assert.Same(selectedMedal, preview.SelectedMedalGuardPreset);
        Assert.Equal(danmuItems, preview.DanmuItems);
        Assert.Equal(superChats, preview.SuperChats);
    }

    /// <summary>
    /// 工作区计数、账号回退和歌词编辑错误刷新显示时不改变名称及编辑内容。
    /// </summary>
    [AvaloniaFact]
    public void WorkspaceAndDialogRefreshWithoutChangingBusinessValues()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        using var workspace = new WorkspaceViewModel(new() { Name = "自定工作区", LiveRooms = [new()] }, _ => "自定账号");
        using var account = new ForwardAccountOption();
        using var editor = new LyricEditDialogViewModel(new() { Title = "歌曲原名", LyricText = "歌词原文" });
        editor.Title = "未完成编辑标题";
        editor.LyricText = "歌词编辑草稿";
        editor.SetLocalizedError("Room.Draft.Failed", "external detail");
        var originalCount = workspace.RoomCountText;
        var originalAccount = account.DisplayName;
        var originalError = editor.ErrorText;

        Localization.SetLanguage("en-US");

        Assert.NotEqual(originalCount, workspace.RoomCountText);
        Assert.NotEqual(originalAccount, account.DisplayName);
        Assert.NotEqual(originalError, editor.ErrorText);
        Assert.Contains("external detail", editor.ErrorText);
        Assert.True(editor.HasError);
        Assert.Equal("自定工作区", workspace.Name);
        Assert.Equal("自定账号", workspace.AccountLabel);
        Assert.Equal("未完成编辑标题", editor.Title);
        Assert.Equal("歌词编辑草稿", editor.LyricText);
    }

    /// <summary>
    /// 快捷键刷新保持正在录制的绑定实例、手势和冲突状态。
    /// </summary>
    [AvaloniaFact]
    public void ShortcutRefreshPreservesRecordingAndBindingState()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var definition = ShortcutActionCatalog.Actions[0];
        var state = new ShortcutBindingState { ActionKey = definition.ActionKey, GestureText = "Ctrl+Alt+X", IsEnabled = true };
        var saveCount = 0;
        var binding = new ShortcutBindingViewModel(definition, state, _ => saveCount++) { IsRecording = true };
        binding.SetConflicted(true);
        var group = new ShortcutBindingGroupViewModel(definition.GroupName, [binding]);
        var originalConflict = binding.ConflictText;
        var changes = new List<string?>();
        binding.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Localization.SetLanguage("en-US");
        group.RefreshLocalization();

        Assert.NotEqual(originalConflict, binding.ConflictText);
        Assert.Contains(nameof(ShortcutBindingViewModel.Name), changes);
        Assert.Contains(nameof(ShortcutBindingViewModel.DisplayGestureText), changes);
        Assert.True(binding.IsRecording);
        Assert.True(binding.IsConflicted);
        Assert.Same(state, binding.Binding);
        Assert.Equal("Ctrl+Alt+X", state.GestureText);
        Assert.Same(binding, Assert.Single(group.Bindings));
        Assert.Equal(0, saveCount);
    }

    /// <summary>
    /// 各级视图模型释放后解除语言事件，不再刷新已经关闭的业务对象。
    /// </summary>
    [AvaloniaFact]
    public void DisposedModelsStopReceivingLanguageChanges()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        using var workspace = new WorkspaceViewModel(new() { LiveRooms = [new()] }, _ => "账号原名");
        using var account = new ForwardAccountOption();
        using var preview = new DanmuStylePreviewViewModel();
        using var editor = new LyricEditDialogViewModel(new() { Title = "歌曲原名", LyricText = "原文" });
        editor.SetLocalizedError("Room.Draft.Failed", "external detail");
        var room = Assert.Single(workspace.Rooms);
        var changes = new List<string?>();
        workspace.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        account.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        preview.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        editor.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        room.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        workspace.Dispose();
        account.Dispose();
        preview.Dispose();
        editor.Dispose();
        changes.Clear();

        Localization.SetLanguage("en-US");

        Assert.Empty(changes);
        Assert.Equal("歌曲原名", editor.Title);
        Assert.Equal("原文", editor.LyricText);
    }
}
