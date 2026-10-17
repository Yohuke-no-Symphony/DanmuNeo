using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证播放器页面与脚本的语言快照、转义边界及既有令牌会话。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class LivePlayerLocalizationTests
{
    /// <summary>
    /// 后台生成页面和更新脚本时使用同一语言的完整快照，并同步首屏语言、连接提示和声音按钮。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("zh-CN", "点击开启声音")]
    [InlineData("en-US", "Click to enable sound")]
    public async Task PageAndBackgroundScriptUseTheSelectedLanguage(string languageId, string soundLabel)
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage(languageId);
        var script = await Task.Run(LivePlayerService.CreateLocalizationScript);
        using var snapshot = ParseScript(script);
        var strings = snapshot.RootElement.GetProperty("strings");
        Assert.Equal(languageId, snapshot.RootElement.GetProperty("languageId").GetString());
        Assert.Equal(soundLabel, strings.GetProperty("Player.EnableSound").GetString());
        Assert.Equal(Localization.Get("Player.NoVideo"), strings.GetProperty("Player.NoVideo").GetString());

        var createHtml = typeof(LivePlayerService).GetMethod("CreatePlayerHtml", BindingFlags.Static | BindingFlags.NonPublic)!;
        var html = await Task.Run(() => Assert.IsType<string>(createHtml.Invoke(null, null)));
        Assert.Contains($"<html lang=\"{languageId}\">", html);
        Assert.Contains($"<div id=\"status\">{WebUtility.HtmlEncode(Localization.Get("Player.Connecting"))}</div>", html);
        Assert.Contains($"<button id=\"soundGate\" type=\"button\">{WebUtility.HtmlEncode(soundLabel)}</button>", html);
        var start = html.IndexOf("let localization = ", StringComparison.Ordinal) + "let localization = ".Length;
        var end = html.IndexOf(";\n", start, StringComparison.Ordinal);
        using var initialSnapshot = JsonDocument.Parse(html[start..end]);
        Assert.Equal(languageId, initialSnapshot.RootElement.GetProperty("languageId").GetString());
        Assert.Equal(soundLabel, initialSnapshot.RootElement.GetProperty("strings").GetProperty("Player.EnableSound").GetString());
    }

    /// <summary>
    /// 用户语言包文本中的引号、标签和 Unicode 分隔符保持为 JSON 数据，不能提前结束脚本。
    /// </summary>
    [Fact]
    public void TranslationJsonEscapesScriptDelimitersAndPreservesTheOriginalText()
    {
        const string TRANSLATION = "\"</script><img src=x onerror=alert(1)>\\\u2028中文";
        var serialize = typeof(LivePlayerService).GetMethod("CreateLocalizationJson", BindingFlags.Static | BindingFlags.NonPublic)!;
        IReadOnlyDictionary<string, string> strings = new Dictionary<string, string>
        {
            ["Player.EnableSound"] = TRANSLATION
        };
        var json = Assert.IsType<string>(serialize.Invoke(null, ["en-US", strings]));

        Assert.DoesNotContain("</script>", json);
        Assert.DoesNotContain("<img", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(TRANSLATION, document.RootElement.GetProperty("strings").GetProperty("Player.EnableSound").GetString());
    }

    /// <summary>
    /// 现有 loopback 页面随语言更新，原令牌仍接受诊断，只有显式撤销才结束该会话。
    /// </summary>
    [AvaloniaFact]
    public async Task LanguageChangesKeepTheExistingPlayerSessionUsable()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        using var service = new LivePlayerService();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var session = await service.CreatePlayerAsync(new()
        {
            RoomId = "localization-offline", StreamUrl = "https://example.invalid/offline.flv"
        }, string.Empty);
        var endpoint = new Uri(new Uri(session.PlayerUrl), $"/diagnostic/{session.Token}");
        var initialPage = await client.GetStringAsync(session.PlayerUrl);
        Assert.Contains("<html lang=\"zh-CN\">", initialPage);

        Localization.SetLanguage("en-US");
        using var script = ParseScript(await Task.Run(LivePlayerService.CreateLocalizationScript));
        var updatedPage = await client.GetStringAsync(session.PlayerUrl);
        Assert.Equal("en-US", script.RootElement.GetProperty("languageId").GetString());
        Assert.Contains("<html lang=\"en-US\">", updatedPage);
        using var diagnostic = await client.PostAsJsonAsync(endpoint,
            new { kind = "warning", message = "Existing session remains active" });
        Assert.Equal(HttpStatusCode.NoContent, diagnostic.StatusCode);

        service.Revoke(session.Token);
        using var revoked = await client.PostAsJsonAsync(endpoint,
            new { kind = "warning", message = "Revoked session" });
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
    }

    private static JsonDocument ParseScript(string script)
    {
        const string PREFIX = "window.yohukeSetLivePlayerLanguage(";
        Assert.StartsWith(PREFIX, script);
        Assert.EndsWith(");", script);
        return JsonDocument.Parse(script[PREFIX.Length..^2]);
    }
}
