using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证应用异常通过发送服务后仍保留语义，外部诊断与业务内容保持原文。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class DanmuSendLocalizationTests
{
    /// <summary>
    /// 配置失败的结果和发送记录可重新呈现当前语言，保留原始异常供日志使用。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfigurationErrorPreservesSemanticOrExternalMessage(bool localized)
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var error = localized
            ? LocalizedExceptionText.Create("Services.BilibiliDanmuConfig.CredentialsIncomplete",
                message => new InvalidOperationException(message))
            : new InvalidOperationException("远端原始错误 / remote-error");
        var originalMessage = error.Message;
        var config = new FakeBilibiliDanmuConfigService { Exception = error };
        var result = await new DanmuSendService(danmuConfigService: config).SendAsync("42", "原始发送内容",
            new() { Cookie = "test-cookie" }, new());

        Localization.SetLanguage("en-US");
        result.Record.RefreshLocalization();

        Assert.False(result.IsAccepted);
        Assert.Null(result.ApiCode);
        Assert.Same(error, result.Error);
        Assert.Equal(originalMessage, error.Message);
        Assert.Equal(LocalizedExceptionText.Get(error), result.ErrorMessage);
        Assert.Equal(result.ErrorMessage, result.Record.Status);
        Assert.Equal("原始发送内容", result.Record.Content);
        Assert.Equal(!localized, originalMessage == result.ErrorMessage);
    }
}
