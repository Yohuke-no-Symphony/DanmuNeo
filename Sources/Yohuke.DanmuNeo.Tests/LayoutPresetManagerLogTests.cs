using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Models.Logging;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证布局管理的校验、操作和确认异常写入会话日志。
/// </summary>
public class LayoutPresetManagerLogTests
{
    /// <summary>
    /// 无效操作保留错误详情，修正后只生成实际成功操作的普通记录。
    /// </summary>
    [AvaloniaFact]
    public async Task InvalidNameAndSuccessfulSaveUseExplicitLevels()
    {
        await using var context = new WorkbenchLayoutTestContext();
        using var log = new AppLogService();
        using var manager = new LayoutPresetManagerViewModel(context.Controller, (_, _) => Task.FromResult(false), log);
        manager.Name = "默认";

        manager.SaveAsCommand.Execute(null);

        Assert.True(manager.HasError);
        var error = Assert.Single(log.Entries);
        Assert.Equal(AppLogLevel.ERROR, error.Level);
        Assert.Contains(nameof(ArgumentException), error.Details);
        manager.Name = "有效名称";
        manager.SaveAsCommand.Execute(null);
        Assert.False(manager.HasError);
        Assert.Equal(AppLogLevel.INFO, log.Entries.Last().Level);
    }

    /// <summary>
    /// 确认删除异常结束忙碌状态并写入诊断，布局仍保留。
    /// </summary>
    [AvaloniaFact]
    public async Task DeleteConfirmationFailureIsRecorded()
    {
        await using var context = new WorkbenchLayoutTestContext();
        var id = context.Controller.SaveAs("保留布局");
        using var log = new AppLogService();
        using var manager = new LayoutPresetManagerViewModel(context.Controller,
            (_, _) => throw new IOException("确认窗口失败"), log);

        await manager.DeleteCommand.ExecuteAsync(null);

        Assert.True(manager.CanEdit);
        Assert.True(manager.HasError);
        Assert.Equal(id, Assert.Single(context.Controller.CaptureFile().UserPresets).Id);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(AppLogLevel.ERROR, entry.Level);
        Assert.Contains("确认窗口失败", entry.Details);
    }

    /// <summary>
    /// 用户正常取消删除不生成错误或成功日志。
    /// </summary>
    [AvaloniaFact]
    public async Task CancelledDeletionDoesNotLogAnOperation()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Controller.SaveAs("未删除布局");
        using var log = new AppLogService();
        using var manager = new LayoutPresetManagerViewModel(context.Controller, (_, _) => Task.FromResult(false), log);

        await manager.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(log.Entries);
        Assert.Single(context.Controller.CaptureFile().UserPresets);
    }
}
