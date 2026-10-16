using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证工作台输入路由让预编辑中的输入法优先处理按键。
/// </summary>
public class WorkbenchCompositionTests
{
    /// <summary>
    /// 有预编辑文本时，Enter 不提交发送，Tab 不轮切预设。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreeditKeepsEnterAndTabAwayFromBusinessCommands(bool floating)
    {
        using var context = new MarkGroupCycleTestContext();
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);

            if (floating)
            {
                workbench.Layouts.FloatPanel(Layouts.WorkbenchPanelIds.COMPOSER);
            }

            var owner = floating ? Assert.Single(workbench.Layouts.FloatingWindows) : main;
            var room = workbench.Context.CurrentRoom!;
            room.InputDraft = "已有草稿";
            workbench.Panels.FocusInputDraft();
            Dispatcher.UIThread.RunJobs();
            var input = workbench.Panels.Composer.Input;
            var presenter = input.GetVisualDescendants().OfType<TextPresenter>().Single();
            presenter.PreeditText = "拼音预编辑";
            owner.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            owner.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Null(room.SendDraftCommand.ExecutionTask);
            Assert.Equal("已有草稿", room.InputDraft);
            presenter.PreeditText = "拼音预编辑";
            owner.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            owner.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);

            Assert.Equal("a", context.ViewModel.SelectedWorkspaceMarkGroup!.Id);
            Assert.Null(context.ViewModel.CycleWorkspaceMarkGroupCommand.ExecutionTask);
            presenter.PreeditText = null;
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }
}
