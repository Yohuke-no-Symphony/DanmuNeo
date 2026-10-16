using Avalonia.Headless.XUnit;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证手动检查更新与自动检查共享请求及独立反馈。
/// </summary>
public class AppUpdateManualTests
{
    /// <summary>
    /// 手动检查忽略启动开关，进行中的重复请求合并。
    /// </summary>
    [AvaloniaFact]
    public async Task ManualCheckIgnoresStartupSettingAndCoalescesRequests()
    {
        using var context = new AppUpdateTestContext("{\"CheckForUpdatesOnStartup\":false}");
        var first = context.ViewModel.CheckForUpdatesManuallyAsync();
        var second = context.ViewModel.CheckForUpdatesManuallyAsync();

        Assert.Same(first, second);
        Assert.Equal(1, context.UpdateService.CallCount);
        Assert.True(context.ViewModel.IsCheckingForUpdates);
        Assert.True(context.ViewModel.IsUpdateDialogOpen);
        context.UpdateService.Response.SetResult(null);
        await first;

        Assert.False(context.ViewModel.IsCheckingForUpdates);
        Assert.True(context.ViewModel.IsUpdateDialogOpen);
        Assert.Contains("最新", context.ViewModel.UpdateCheckMessage);
    }

    /// <summary>
    /// 手动加入启动请求后，即使关闭启动开关也保留手动检查结果。
    /// </summary>
    [AvaloniaFact]
    public async Task ManualCheckJoinsStartupAndRetainsItsOwnIntent()
    {
        using var context = new AppUpdateTestContext();
        var automatic = context.ViewModel.CheckForUpdatesOnStartupAsync();
        var manual = context.ViewModel.CheckForUpdatesManuallyAsync();
        context.ViewModel.CheckForUpdatesOnStartup = false;

        Assert.Same(automatic, manual);
        Assert.False(context.UpdateService.CancellationToken.IsCancellationRequested);
        context.UpdateService.Response.SetResult(context.Update);
        await manual;

        Assert.Same(context.Update, context.ViewModel.AvailableUpdate);
        Assert.True(context.ViewModel.IsUpdateDialogOpen);
    }

    /// <summary>
    /// 手动检查失败提供可见反馈，不覆盖工作区状态。
    /// </summary>
    [AvaloniaFact]
    public async Task ManualFailureIsVisible()
    {
        using var context = new AppUpdateTestContext();
        context.ViewModel.StatusMessage = "正在同传";
        context.UpdateService.Response.SetException(new HttpRequestException("offline"));

        await context.ViewModel.CheckForUpdatesManuallyAsync();

        Assert.True(context.ViewModel.IsUpdateDialogOpen);
        Assert.Contains("失败", context.ViewModel.UpdateCheckMessage);
        Assert.False(context.ViewModel.IsCheckingForUpdates);
        Assert.Equal("正在同传", context.ViewModel.StatusMessage);
    }

    /// <summary>
    /// 关闭检查窗口取消本次请求，迟到结果不重新打开窗口。
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingManualCheckRejectsLateResult()
    {
        using var context = new AppUpdateTestContext();
        var check = context.ViewModel.CheckForUpdatesManuallyAsync();
        context.ViewModel.CloseUpdateDialog();

        Assert.True(context.UpdateService.CancellationToken.IsCancellationRequested);
        context.UpdateService.Response.SetResult(context.Update);
        await check;

        Assert.False(context.ViewModel.IsUpdateDialogOpen);
        Assert.Null(context.ViewModel.AvailableUpdate);
    }

    /// <summary>
    /// 退出应用后拒绝手动检查的迟到结果。
    /// </summary>
    [AvaloniaFact]
    public async Task DisposalRejectsManualResult()
    {
        using var context = new AppUpdateTestContext();
        var check = context.ViewModel.CheckForUpdatesManuallyAsync();
        context.ViewModel.Dispose();
        context.UpdateService.Response.SetResult(context.Update);
        await check;

        Assert.False(context.ViewModel.IsUpdateDialogOpen);
        Assert.Null(context.ViewModel.AvailableUpdate);
    }
}
