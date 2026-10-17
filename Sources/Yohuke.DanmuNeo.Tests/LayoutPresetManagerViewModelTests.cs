using System.Threading;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证布局管理的选择、只读命令、草稿及删除确认生命周期。
/// </summary>
public class LayoutPresetManagerViewModelTests
{
    /// <summary>
    /// 管理器默认选中当前布局，内置布局只允许应用，批量自定义选择只允许删除。
    /// </summary>
    [AvaloniaFact]
    public async Task SelectionControlsReadOnlyAndMultipleSelectionCommands()
    {
        await using var context = new WorkbenchLayoutTestContext();
        var first = context.Controller.SaveAs("自用一");
        var second = context.Controller.SaveAs("自用二");
        using var manager = new LayoutPresetManagerViewModel(context.Controller, (_, _) => Task.FromResult(true));

        Assert.Equal(6, manager.Presets.Count);
        Assert.Equal(second, manager.Selected!.Id);
        Assert.Equal("自用二", manager.Name);
        Assert.Equal(4, manager.Presets.Count(row => row.IsReadOnly));
        Assert.All(manager.Presets.Where(row => row.IsReadOnly), row => Assert.Contains("内置", row.DisplayName));
        Assert.True(manager.ApplyCommand.CanExecute(null));
        Assert.True(manager.RenameCommand.CanExecute(null));

        manager.Selected = manager.Presets.First(row => row.IsReadOnly);
        Assert.True(manager.ApplyCommand.CanExecute(null));
        Assert.False(manager.RenameCommand.CanExecute(null));
        Assert.False(manager.DeleteCommand.CanExecute(null));
        Assert.Empty(manager.Name);

        manager.SetSelection(first, [first, second]);
        Assert.False(manager.ApplyCommand.CanExecute(null));
        Assert.False(manager.RenameCommand.CanExecute(null));
        Assert.True(manager.DeleteCommand.CanExecute(null));
        Assert.True(manager.SaveAsCommand.CanExecute(null));

        manager.SetSelection(first, [first, WorkspaceLayoutPresets.DEFAULT]);
        Assert.False(manager.DeleteCommand.CanExecute(null));
        manager.SetSelection(null, []);
        Assert.Null(manager.Selected);
        Assert.Empty(manager.SelectedIds);
        Assert.False(manager.ApplyCommand.CanExecute(null));
        Assert.False(manager.DeleteCommand.CanExecute(null));
        Assert.True(manager.SaveAsCommand.CanExecute(null));
    }

    /// <summary>
    /// 应用所选预设复用现有布局能力，另存保存当前排布并选中新建布局。
    /// </summary>
    [AvaloniaFact]
    public async Task ApplyAndSaveAsUseTheSelectedPresetAndCurrentLayout()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        using var manager = new LayoutPresetManagerViewModel(context.Controller, (_, _) => Task.FromResult(false));
        manager.Selected = manager.Presets.Single(row => row.Id == WorkspaceLayoutPresets.TRANSLATION);
        manager.ApplyCommand.Execute(null);
        context.Pump();
        Assert.Equal(WorkspaceLayoutPresets.TRANSLATION, context.Controller.CurrentPresetId);
        Assert.False(context.Controller.IsVisible(WorkbenchPanelIds.LYRICS));
        context.Controller.HidePanel(WorkbenchPanelIds.HISTORY);
        context.Pump();

        manager.Name = "  同传新排布  ";
        manager.SaveAsCommand.Execute(null);

        var saved = Assert.Single(context.Controller.CaptureFile().UserPresets);
        Assert.Equal("同传新排布", saved.Name);
        Assert.Contains(WorkbenchPanelIds.HISTORY, saved.Layout.HiddenPanelIds);
        Assert.Equal(saved.Id, manager.Selected!.Id);
        Assert.Equal(new[] { saved.Id }, manager.SelectedIds);
        Assert.Equal(saved.Name, manager.Name);
        Assert.False(manager.HasError);
    }

    /// <summary>
    /// 无效名称不会写入，布局自动更新保留名称草稿，成功重命名同步列表并保持选择。
    /// </summary>
    [AvaloniaFact]
    public async Task RenameValidatesNamesAndRetainsDraftAcrossControllerRefreshes()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var id = context.Controller.SaveAs("原名");
        using var manager = new LayoutPresetManagerViewModel(context.Controller, (_, _) => Task.FromResult(false));
        manager.Name = "默认";
        manager.RenameCommand.Execute(null);
        Assert.True(manager.HasError);
        Assert.Equal("原名", context.Controller.Presets.Single(row => row.Id == id).Name);
        var renameError = manager.Error;
        context.Controller.HidePanel(WorkbenchPanelIds.LYRICS);
        context.Pump();
        Assert.Equal(renameError, manager.Error);
        Assert.Equal("默认", manager.Name);

        manager.Name = "  未提交的新名  ";
        Assert.False(manager.HasError);
        context.Controller.HidePanel(WorkbenchPanelIds.HISTORY);
        context.Pump();
        Assert.Equal("  未提交的新名  ", manager.Name);
        Assert.Equal(id, manager.Selected!.Id);
        manager.RenameCommand.Execute(null);

        Assert.Equal(id, manager.Selected!.Id);
        Assert.Equal("未提交的新名", manager.Selected.Name);
        Assert.Equal("未提交的新名", manager.Name);
        Assert.False(manager.HasError);
    }

    /// <summary>
    /// 取消删除不改动布局，确认时一次提交所选自定义布局并恢复可用选择。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteConfirmsAllNamesBeforeUpdatingTheSelectedPresets(bool confirm)
    {
        await using var context = new WorkbenchLayoutTestContext();
        var first = context.Controller.SaveAs("第一个");
        var second = context.Controller.SaveAs("第二个");
        IReadOnlyList<string>? requestedNames = null;
        using var manager = new LayoutPresetManagerViewModel(context.Controller, (names, _) =>
        {
            requestedNames = names;
            return Task.FromResult(confirm);
        });
        manager.SetSelection(second, [second, first]);
        manager.Name = "保留草稿";
        await manager.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "第一个", "第二个" }, requestedNames);
        Assert.Equal(confirm ? 4 : 6, manager.Presets.Count);
        Assert.Equal(confirm ? WorkspaceLayoutPresets.DEFAULT : second, context.Controller.CurrentPresetId);
        Assert.Equal(confirm ? WorkspaceLayoutPresets.DEFAULT : second, manager.Selected!.Id);
        Assert.Equal(confirm ? "" : "保留草稿", manager.Name);
        Assert.True(manager.CanEdit);
        Assert.False(manager.HasError);
    }

    /// <summary>
    /// 等待确认时锁定管理操作，关闭管理器取消等待，迟到的确认不能删除预设。
    /// </summary>
    [AvaloniaFact]
    public async Task DisposeCancelsPendingConfirmationAndIgnoresLateApproval()
    {
        await using var context = new WorkbenchLayoutTestContext();
        var id = context.Controller.SaveAs("不能误删");
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = CancellationToken.None;
        using var manager = new LayoutPresetManagerViewModel(context.Controller, (_, cancellationToken) =>
        {
            token = cancellationToken;
            return confirmation.Task;
        });
        var deletion = manager.DeleteCommand.ExecuteAsync(null);
        Assert.False(manager.CanEdit);
        Assert.False(manager.ApplyCommand.CanExecute(null));
        Assert.False(manager.RenameCommand.CanExecute(null));
        Assert.False(manager.DeleteCommand.CanExecute(null));
        Assert.False(manager.SaveAsCommand.CanExecute(null));
        manager.SetSelection(WorkspaceLayoutPresets.DEFAULT, [WorkspaceLayoutPresets.DEFAULT]);
        Assert.Equal(id, manager.Selected!.Id);

        manager.Dispose();
        await deletion.WaitAsync(TimeSpan.FromSeconds(5));
        confirmation.SetResult(true);

        Assert.True(token.IsCancellationRequested);
        Assert.False(manager.CanEdit);
        Assert.Equal(id, Assert.Single(context.Controller.CaptureFile().UserPresets).Id);
        context.Controller.SaveAs("关闭后创建");
        Assert.Equal(5, manager.Presets.Count);
    }

    /// <summary>
    /// 确认期间被其他入口删除的目标导致整批拒绝，不影响仍存在的布局。
    /// </summary>
    [AvaloniaFact]
    public async Task StaleConfirmationCannotPartiallyDeleteRemainingTargets()
    {
        await using var context = new WorkbenchLayoutTestContext();
        var first = context.Controller.SaveAs("仍然保留");
        var second = context.Controller.SaveAs("外部删除");
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = new LayoutPresetManagerViewModel(context.Controller, (_, _) => confirmation.Task);
        manager.SetSelection(first, [first, second]);
        var deletion = manager.DeleteCommand.ExecuteAsync(null);
        context.Controller.Delete(second);
        confirmation.SetResult(true);
        await deletion;

        Assert.Equal(first, Assert.Single(context.Controller.CaptureFile().UserPresets).Id);
        Assert.True(manager.HasError);
        Assert.True(manager.CanEdit);
    }

    /// <summary>
    /// 列表因外部改名和另存刷新时保留多选、主选项和未提交的名称。
    /// </summary>
    [AvaloniaFact]
    public async Task MultipleSelectionAndNameDraftSurviveControllerRefresh()
    {
        await using var context = new WorkbenchLayoutTestContext();
        var first = context.Controller.SaveAs("第一项");
        var second = context.Controller.SaveAs("第二项");
        using var manager = new LayoutPresetManagerViewModel(context.Controller, (_, _) => Task.FromResult(false));
        manager.SetSelection(second, [first, second]);
        manager.Name = "尚未保存的名称";

        context.Controller.Rename(first, "外部重命名");
        context.Controller.SaveAs("外部新建");

        Assert.Equal(new[] { first, second }, manager.SelectedIds);
        Assert.Same(manager.Presets.Single(row => row.Id == second), manager.Selected);
        Assert.Equal("尚未保存的名称", manager.Name);
        Assert.True(manager.DeleteCommand.CanExecute(null));
        Assert.False(manager.RenameCommand.CanExecute(null));
    }

    /// <summary>
    /// 延迟保存失败显示错误并保留内存修改；释放管理器后不再接收后续错误。
    /// </summary>
    [AvaloniaFact]
    public async Task PersistenceFailuresAreVisibleUntilManagerIsDisposed()
    {
        string? writeError = null;
        await using var context = new WorkbenchLayoutTestContext(replaceFile: (source, destination) =>
        {
            if (writeError is not null)
            {
                throw new IOException(writeError);
            }

            File.Move(source, destination, true);
        });
        await context.Controller.FlushAsync();
        using var manager = new LayoutPresetManagerViewModel(context.Controller, (_, _) => Task.FromResult(false));
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Controller.PersistenceFailed += exception => failure.TrySetResult(exception);

        try
        {
            writeError = "第一次保存失败";
            var first = context.Controller.SaveAs("内存中保留");
            await failure.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.Pump();
            Assert.Equal(writeError, manager.Error);
            Assert.True(manager.HasError);
            Assert.True(manager.CanEdit);
            Assert.Contains(manager.Presets, row => row.Id == first);
            Assert.Empty((await context.Store.LoadAsync()).UserPresets);

            manager.Dispose();
            var displayedError = manager.Error;
            failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
            writeError = "关闭后的保存失败";
            context.Controller.SaveAs("关闭后新建");
            await failure.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.Pump();
            Assert.Equal(displayedError, manager.Error);
            Assert.Equal(5, manager.Presets.Count);
        }
        finally
        {
            writeError = null;
            await context.Controller.FlushAsync();
        }
    }
}
