using System.Reflection;
using System.Text.Json;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

public class LivePlayerServiceTests
{
    [Fact]
    public void PlayerHtmlHidesNativeControlsByDefault()
    {
        var method = typeof(LivePlayerService).GetMethod("CreatePlayerHtml", BindingFlags.NonPublic | BindingFlags.Static);
        var html = Assert.IsType<string>(method?.Invoke(null, null));

        Assert.Contains("<video id=\"player\" autoplay muted playsinline>", html);
        Assert.DoesNotContain("<video id=\"player\" controls", html);
        var script = LivePlayerService.CreateLocalizationScript();
        using var snapshot = JsonDocument.Parse(script["window.yohukeSetLivePlayerLanguage(".Length..^2]);
        Assert.Equal(Localization.Get("Player.EnableSound"),
            snapshot.RootElement.GetProperty("strings").GetProperty("Player.EnableSound").GetString());
        Assert.Contains("text(\"Player.EnableSound\")", html);
        Assert.Contains("root.addEventListener(\"pointerdown\", unlockSoundByUserGesture);", html);
        Assert.Contains("window.yohukeSetLivePlayerAudio", html);
        Assert.Contains("requestedMuted || !soundUnlocked", html);
        Assert.DoesNotContain("正在开启声音", html);
    }

    [Fact]
    public void PlayerHtmlDetectsMissingVideoFrames()
    {
        var method = typeof(LivePlayerService).GetMethod("CreatePlayerHtml", BindingFlags.NonPublic | BindingFlags.Static);
        var html = Assert.IsType<string>(method?.Invoke(null, null));

        Assert.Contains("video.videoWidth", html);
        Assert.Contains("video.videoHeight", html);
        Assert.Contains("lastVideoFrameAt", html);
        Assert.Contains("lastDecodedFrames", html);
        Assert.Contains("mpegts.Events.MEDIA_INFO", html);
        Assert.Contains("mpegts.Events.STATISTICS_INFO", html);
        Assert.Contains("recoverFromStuck(\"Player.NoVideo\")", html);
        var script = LivePlayerService.CreateLocalizationScript();
        using var snapshot = JsonDocument.Parse(script["window.yohukeSetLivePlayerLanguage(".Length..^2]);
        Assert.Equal(Localization.Get("Player.NoVideo"),
            snapshot.RootElement.GetProperty("strings").GetProperty("Player.NoVideo").GetString());
    }
}
