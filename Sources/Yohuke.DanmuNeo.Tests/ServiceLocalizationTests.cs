using System.Net;
using Avalonia.Headless.XUnit;
using Newtonsoft.Json;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证离线服务的本地诊断随当前语言生成，协议值、远端错误和业务数据保持原样。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class ServiceLocalizationTests
{
    /// <summary>
    /// 浏览器登录使用当前语言显示名称与诊断，保持登录 Cookie 提取结果不变。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void BrowserLoginUsesSelectedLanguageAndPreservesCookieFields(string languageID)
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage(languageID);
        var service = new BrowserCookieLoginService();

        Assert.Equal(Localization.Get("Services.BrowserLogin.Bilibili"),
            service.GetPlatformName(BrowserLoginPlatform.Bilibili));
        var error = Assert.Throws<InvalidOperationException>(() =>
            service.CreateResult(BrowserLoginPlatform.Bilibili, []));
        Assert.Equal(Localization.Get("Services.BrowserLogin.BilibiliCookiesMissing"), error.Message);

        var result = service.CreateResult(BrowserLoginPlatform.Bilibili,
        [
            new Cookie("SESSDATA", "test-session"),
            new Cookie("bili_jct", "test-csrf"),
            new Cookie("DedeUserId", "42")
        ]);

        Assert.Equal("buvid3=;SESSDATA=test-session;bili_jct=test-csrf;DedeUserId=42", result.Cookie);
        Assert.Equal(Localization.Get("Services.BrowserLogin.BilibiliCookiesRead"), result.Message);
        Assert.Equal("Services.BrowserLogin.BilibiliCookiesRead", result.MessageKey);
    }

    /// <summary>
    /// 歌词校验组合当前语言的上下文和诊断，保留输入内容。
    /// </summary>
    [AvaloniaFact]
    public void LyricValidationRegeneratesLocalizedDiagnosticWithoutChangingTheInput()
    {
        using var language = new LocalizationTestScope();
        var service = new LyricLibraryService();
        var item = new LyricLibraryItem { Title = "输入曲名", LyricText = "[00:01", Tags = "原始标签" };
        Localization.SetLanguage("zh-CN");
        var chinese = Assert.Throws<InvalidDataException>(() => service.Validate(item));

        Localization.SetLanguage("en-US");
        var english = Assert.Throws<InvalidDataException>(() => service.Validate(item));

        Assert.Equal("原文歌词存在不完整的时间轴标记。", chinese.Message);
        Assert.Equal(Localization.Format("Services.LyricLibrary.IncompleteTimestamp",
            Localization.Get("Services.LyricLibrary.OriginalLyrics")), english.Message);
        Assert.NotEqual(chinese.Message, english.Message);
        Assert.Equal("输入曲名", item.Title);
        Assert.Equal("[00:01", item.LyricText);
        Assert.Equal("原始标签", item.Tags);
    }

    /// <summary>
    /// 剪贴板规则错误的条目位置和对象校验说明都使用当前语言。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void ClipboardDiagnosticLocalizesTheRuleContext(string languageID)
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage(languageID);
        const string TEXT = """
            {"Format":"Yohuke.DanmuNeo.ForwardRules","Version":1,"Rules":[null]}
            """;

        var error = Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(TEXT));

        Assert.Equal(Localization.Format("Services.ForwardRuleClipboard.ObjectRequired",
            Localization.Format("Services.ForwardRuleClipboard.RuleContext", 1)), error.Message);
    }

    /// <summary>
    /// B 站接口返回的错误原文不会被翻译或改写。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public async Task UserNameServicePreservesRemoteErrorMessage(string languageID)
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage(languageID);
        using var handler = new LiveRoomSearchHttpHandler
        {
            ResponseJson = """{"code":-400,"message":"远端原文 / remote-message"}"""
        };
        using var client = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BilibiliUserNameService(client).GetUserNameAsync(42));

        Assert.Equal("远端原文 / remote-message", error.Message);
        Localization.SetLanguage(languageID == "zh-CN" ? "en-US" : "zh-CN");
        Assert.Equal("远端原文 / remote-message", LocalizedExceptionText.Get(error));
        Assert.Equal("远端原文 / remote-message", error.Message);
    }

    /// <summary>
    /// 音乐接口的本地错误包装使用当前语言，保留远端原因和请求关键词。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public async Task MusicSearchLocalizesOperationAndPreservesRemoteReasonAndKeyword(string languageID)
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage(languageID);
        using var handler = new LiveRoomSearchHttpHandler
        {
            ResponseJson = """{"code":403,"message":"版权受限 / remote-reason"}"""
        };
        using var client = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MusicLyricImportService(client).SearchAsync("原始查询 key", "wy", ""));

        Assert.Equal(Localization.Format("Services.MusicLyrics.OperationFailedWithReason",
            Localization.Get("Services.MusicLyrics.NetEaseSearch"), 403, "版权受限 / remote-reason"), error.Message);
        Assert.Contains("s=原始查询 key", WebUtility.UrlDecode(handler.RequestUri!.Query));
    }

    /// <summary>
    /// 中英文环境导入相同歌词时保存相同业务内容和兼容的原来源标签。
    /// </summary>
    [AvaloniaFact]
    public async Task MusicImportKeepsLyricsMetadataAndPersistentTagsIndependentOfUiLanguage()
    {
        using var language = new LocalizationTestScope();
        using var handler = new LiveRoomSearchHttpHandler
        {
            ResponseJson = """{"code":200,"lrc":{"lyric":"[00:01]原文"},"tlyric":{"lyric":"[00:01]译文"}}"""
        };
        using var client = new HttpClient(handler);
        var service = new MusicLyricImportService(client);
        var result = new MusicLyricSearchResult
        {
            Source = "wy", SourceSongId = "42", Title = "输入曲名", Artist = "输入歌手", Album = "输入专辑"
        };
        Localization.SetLanguage("zh-CN");
        var chinese = await service.ImportAsync(result, "");

        Localization.SetLanguage("en-US");
        var english = await service.ImportAsync(result, "");

        Assert.Equal(chinese.Title, english.Title);
        Assert.Equal(chinese.Artist, english.Artist);
        Assert.Equal(chinese.Album, english.Album);
        Assert.Equal("wy", english.Source);
        Assert.Equal("42", english.SourceSongId);
        Assert.Equal("[00:01]原文", english.LyricText);
        Assert.Equal("[00:01]译文", english.TranslatedLyricText);
        Assert.Equal(chinese.Tags, english.Tags);
        Assert.Equal("输入曲名 输入歌手 输入专辑 网易云", english.Tags);
        Assert.True(english.HasTranslation);
    }

    /// <summary>
    /// XML 的双语标识属于文件协议，英文界面下仍按原值解析且保留歌词内容。
    /// </summary>
    [AvaloniaFact]
    public async Task EnglishInterfacePreservesTheLocalXmlBilingualProtocolValue()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("en-US");
        var path = Path.Combine(Path.GetTempPath(), "danmu-i18n-lyrics-" + Guid.NewGuid().ToString("N") + ".xml");

        try
        {
            await File.WriteAllTextAsync(path, """
                <local><name>原始曲名</name><artists>原始歌手</artists><tags>原始标签</tags><type>双语</type><lyric>[00:01]原文
                [00:01]译文</lyric></local>
                """);

            var item = await new LyricLibraryService().CreateFromLocalFileAsync(path);

            Assert.True(item.HasTranslation);
            Assert.Equal("原始曲名", item.Title);
            Assert.Equal("原始歌手", item.Artist);
            Assert.Equal("原始标签", item.Tags);
            Assert.Equal("[00:01]原文", item.LyricText);
            Assert.Equal("[00:01]译文", item.TranslatedLyricText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 空 JSON 的应用诊断使用当前语言并保留反序列化异常类型。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public async Task EmptyApiJsonUsesLocalizedDiagnosticAndPreservesExceptionType(string languageID)
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage(languageID);
        using var handler = new LiveRoomSearchHttpHandler { ResponseJson = "null" };
        using var client = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<JsonSerializationException>(() =>
            new BilibiliUserNameService(client).GetUserNameAsync(42));

        Assert.Equal(Localization.Get("Services.Api.EmptyJsonResult"), error.Message);
    }

    /// <summary>
    /// QQ 登录请求中的服务端页面参数保留约定值，界面语言不会改写请求。
    /// </summary>
    [AvaloniaFact]
    public async Task EnglishInterfaceDoesNotTranslateTheQqLoginProtocolParameter()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("en-US");
        using var handler = new LiveRoomSearchHttpHandler();
        using var client = new HttpClient(handler);
        using var api = new QQMusicApi(httpClient: client);

        await api.PrepareLoginAsync();

        Assert.Contains("login_text=登录", WebUtility.UrlDecode(handler.RequestUri!.Query));
    }

    /// <summary>
    /// 界面语言不会修改转发发送内容、用户正则或剪贴板协议文本。
    /// </summary>
    [AvaloniaFact]
    public void ForwardingPayloadAndClipboardWireTextRemainIndependentOfUiLanguage()
    {
        using var language = new LocalizationTestScope();
        var service = new DanmuForwardService();
        var message = new BilibiliDanmuMessage { Uid = 42, Content = "【原始弹幕】" };
        var rule = new DanmuForwardRuleState { SenderUid = "42", ContentPattern = "^【.*?】$" };
        var symbols = new MarkSymbolGroup { TranslateOpenMark = "《", TranslateCloseMark = "》" };
        Localization.SetLanguage("zh-CN");
        var chinesePayload = service.CreateForwardMessage(message, rule, symbols, []);
        var chineseClipboard = DanmuForwardRuleClipboardService.Serialize([rule]);

        Localization.SetLanguage("en-US");
        var englishPayload = service.CreateForwardMessage(message, rule, symbols, []);
        var englishClipboard = DanmuForwardRuleClipboardService.Serialize([rule]);

        Assert.True(service.IsMatch(message, rule));
        Assert.Equal("《【原始弹幕】》", englishPayload);
        Assert.Equal(chinesePayload, englishPayload);
        Assert.Equal(chineseClipboard, englishClipboard);
        var restored = Assert.Single(DanmuForwardRuleClipboardService.Deserialize(englishClipboard));
        Assert.Equal(rule.SenderUid, restored.SenderUid);
        Assert.Equal(rule.ContentPattern, restored.ContentPattern);
        Assert.False(restored.IsEnabled);
    }

    /// <summary>
    /// 已捕获歌词异常保留原类型和原 Message，通过语义入口重新呈现当前语言与歌词名称。
    /// </summary>
    [AvaloniaFact]
    public void CapturedLyricExceptionRendersTheCurrentLanguageWithoutChangingItsTypeOrOriginalMessage()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var item = new LyricLibraryItem { Title = "原始曲名", LyricText = "[00:01" };
        var error = Assert.Throws<InvalidDataException>(() => new LyricLibraryService().Validate(item));
        var originalMessage = error.Message;

        Localization.SetLanguage("en-US");

        Assert.IsType<InvalidDataException>(error);
        Assert.Equal(originalMessage, error.Message);
        Assert.Equal(Localization.Format("Services.LyricLibrary.IncompleteTimestamp",
            Localization.Get("Services.LyricLibrary.OriginalLyrics")), LocalizedExceptionText.Get(error));
        Assert.NotEqual(originalMessage, LocalizedExceptionText.Get(error));
        var remote = new InvalidDataException("远端原始错误 / remote-error");
        Assert.Equal(remote.Message, LocalizedExceptionText.Get(remote));
    }

    /// <summary>
    /// 已捕获剪贴板错误重新呈现包名称或规则位置，同时保留原始异常文本和类型。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("[]", false)]
    [InlineData("{\"Format\":\"Yohuke.DanmuNeo.ForwardRules\",\"Version\":1,\"Rules\":[null]}", true)]
    public void CapturedClipboardErrorRefreshesTheEntireContext(string text, bool isRuleContext)
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var error = Assert.Throws<InvalidDataException>(() => DanmuForwardRuleClipboardService.Deserialize(text));
        var original = error.Message;

        Localization.SetLanguage("en-US");

        var context = isRuleContext
            ? Localization.Format("Services.ForwardRuleClipboard.RuleContext", 1)
            : Localization.Get("Services.ForwardRuleClipboard.PackageContext");
        Assert.Equal(Localization.Format("Services.ForwardRuleClipboard.ObjectRequired", context),
            LocalizedExceptionText.Get(error));
        Assert.NotEqual(original, LocalizedExceptionText.Get(error));
        Assert.Equal(original, error.Message);
        Assert.IsType<InvalidDataException>(error);
    }

    /// <summary>
    /// 音乐错误的操作名和来源随语言更新，远端原因原文及捕获时来源保持不变。
    /// </summary>
    [AvaloniaFact]
    public async Task CapturedMusicErrorsRefreshOperationAndSourceWhilePreservingTheRemoteReason()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        using var handler = new LiveRoomSearchHttpHandler
        {
            ResponseJson = """{"code":403,"message":"版权受限 / remote-reason"}"""
        };
        using var client = new HttpClient(handler);
        var service = new MusicLyricImportService(client);
        var operationError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SearchAsync("原始查询", "wy", ""));
        handler.ResponseJson = """{"code":200,"lrc":{"lyric":""}}""";
        var result = new MusicLyricSearchResult { Source = "wy", SourceSongId = "42" };
        var sourceError = await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(result, ""));
        var originalOperationMessage = operationError.Message;
        var originalSourceMessage = sourceError.Message;
        result.Source = "qq";

        Localization.SetLanguage("en-US");

        Assert.Equal(Localization.Format("Services.MusicLyrics.OperationFailedWithReason",
            Localization.Get("Services.MusicLyrics.NetEaseSearch"), 403, "版权受限 / remote-reason"),
            LocalizedExceptionText.Get(operationError));
        Assert.Equal(Localization.Format("Services.MusicLyrics.LyricsMissing", Localization.Get("App.Music.Source.wy")),
            LocalizedExceptionText.Get(sourceError));
        Assert.NotEqual(originalOperationMessage, LocalizedExceptionText.Get(operationError));
        Assert.NotEqual(originalSourceMessage, LocalizedExceptionText.Get(sourceError));
        Assert.Equal(originalOperationMessage, operationError.Message);
        Assert.Equal(originalSourceMessage, sourceError.Message);
    }

    /// <summary>
    /// 带状态码的弹幕配置错误重新呈现本地操作名，保留同一异常中的原始 Message。
    /// </summary>
    [AvaloniaFact]
    public async Task CapturedDanmuConfigurationErrorRefreshesFallbackAndKeepsTheStatusCode()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        using var handler = new DanmuConfigHttpHandler();
        handler.Responses.Enqueue("""{"code":-400}""");
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BilibiliDanmuConfigService(client).GetConfigAsync("100", "SESSDATA=test-session"));
        var original = error.Message;

        Localization.SetLanguage("en-US");

        Assert.Equal(Localization.Format("Services.BilibiliDanmuConfig.FailureWithCode",
            Localization.Get("Services.BilibiliDanmuConfig.OptionsReadFailed"), -400), LocalizedExceptionText.Get(error));
        Assert.NotEqual(original, LocalizedExceptionText.Get(error));
        Assert.Equal(original, error.Message);
    }
}
