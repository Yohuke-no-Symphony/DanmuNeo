using Avalonia.Headless.XUnit;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证后台更新检查的启动次数、设置兼容和生命周期。
/// </summary>
public class AppUpdateStartupTests
{
    /// <summary>
    /// 构造视图模型不会发起网络请求，旧配置缺字段时默认开启。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("{}")]
    public void ConstructionKeepsChecksExplicitAndDefaultEnabled(string? settingsJson)
    {
        using var context = new AppUpdateTestContext(settingsJson);

        Assert.True(context.ViewModel.CheckForUpdatesOnStartup);
        Assert.Equal(0, context.UpdateService.CallCount);
    }

    /// <summary>
    /// 关闭的开关在加载和保存后保留，启动不发请求。
    /// </summary>
    [AvaloniaFact]
    public async Task DisabledSettingIsPreservedAndSkipsRequest()
    {
        using var context = new AppUpdateTestContext("{\"CheckForUpdatesOnStartup\":false}");

        await context.ViewModel.CheckForUpdatesOnStartupAsync();
        await context.ViewModel.CloseSettingsAsync();

        Assert.Equal(0, context.UpdateService.CallCount);
        Assert.False(context.StateService.Load().Settings.CheckForUpdatesOnStartup);
    }

    /// <summary>
    /// 待完成请求不阻塞调用，重复调用及关闭提示后均不重新检查。
    /// </summary>
    [AvaloniaFact]
    public async Task StartupChecksOnceAndDoesNotRepeatDismissedPrompt()
    {
        using var context = new AppUpdateTestContext();
        var first = context.ViewModel.CheckForUpdatesOnStartupAsync();
        var second = context.ViewModel.CheckForUpdatesOnStartupAsync();

        Assert.Same(first, second);
        Assert.False(first.IsCompleted);
        Assert.Equal(1, context.UpdateService.CallCount);
        Assert.Equal(context.ViewModel.GetType().Assembly.GetName().Version, context.UpdateService.CurrentVersion);

        context.UpdateService.Response.SetResult(context.Update);
        await first;

        Assert.True(context.ViewModel.IsUpdateDialogOpen);
        Assert.Same(context.Update, context.ViewModel.AvailableUpdate);
        context.ViewModel.CloseUpdateDialog();
        await context.ViewModel.CheckForUpdatesOnStartupAsync();

        Assert.False(context.ViewModel.IsUpdateDialogOpen);
        Assert.Equal(1, context.UpdateService.CallCount);
    }

    /// <summary>
    /// 没有新版本时保持安静。
    /// </summary>
    [AvaloniaFact]
    public async Task NoUpdateDoesNotShowPrompt()
    {
        using var context = new AppUpdateTestContext();
        context.UpdateService.Response.SetResult(null);

        await context.ViewModel.CheckForUpdatesOnStartupAsync();

        Assert.False(context.ViewModel.IsUpdateDialogOpen);
        Assert.Null(context.ViewModel.AvailableUpdate);
    }

    /// <summary>
    /// 请求异常和超时不弹出错误框，也不覆盖已有工作状态。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCheckEndsSilently(bool timeout)
    {
        using var context = new AppUpdateTestContext();
        context.ViewModel.StatusMessage = "当前工作状态";
        context.UpdateService.Response.SetException(timeout
            ? new TaskCanceledException("timeout")
            : new HttpRequestException("offline"));

        await context.ViewModel.CheckForUpdatesOnStartupAsync();

        Assert.False(context.ViewModel.IsUpdateDialogOpen);
        Assert.Null(context.ViewModel.AvailableUpdate);
        Assert.Equal("当前工作状态", context.ViewModel.StatusMessage);
    }

    /// <summary>
    /// 检查中关闭开关会取消请求，随后重开也不接受迟到结果。
    /// </summary>
    [AvaloniaFact]
    public async Task DisablingSettingRejectsLateResponse()
    {
        using var context = new AppUpdateTestContext();
        var check = context.ViewModel.CheckForUpdatesOnStartupAsync();

        context.ViewModel.CheckForUpdatesOnStartup = false;
        Assert.True(context.UpdateService.CancellationToken.IsCancellationRequested);
        context.ViewModel.CheckForUpdatesOnStartup = true;
        context.UpdateService.Response.SetResult(context.Update);
        await check;

        Assert.False(context.ViewModel.IsUpdateDialogOpen);
        Assert.Null(context.ViewModel.AvailableUpdate);
    }

    /// <summary>
    /// 应用释放后即使请求忽略取消并返回，也不能再打开提示。
    /// </summary>
    [AvaloniaFact]
    public async Task DisposalCancelsRequestAndRejectsLateResponse()
    {
        using var context = new AppUpdateTestContext();
        var check = context.ViewModel.CheckForUpdatesOnStartupAsync();

        context.ViewModel.Dispose();
        Assert.True(context.UpdateService.CancellationToken.IsCancellationRequested);
        context.UpdateService.Response.SetResult(context.Update);
        await check;

        Assert.False(context.ViewModel.IsUpdateDialogOpen);
        Assert.Null(context.ViewModel.AvailableUpdate);
    }
}