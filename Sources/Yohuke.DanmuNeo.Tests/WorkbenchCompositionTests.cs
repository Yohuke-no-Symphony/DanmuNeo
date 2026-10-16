using Avalonia.Controls;
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
/// 验证工作台输入路由和提示文字在输入法预编辑期间保持正确状态。
/// </summary>
public class WorkbenchCompositionTests
{
    /// <summary>
    /// 空草稿开始预编辑时隐藏提示，取消后恢复，提交文字后保持隐藏。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyDraftWatermarkFollowsCompositionStartCancelAndCommit(bool floating)
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
            room.InputDraft = "";
            workbench.Panels.FocusInputDraft();
            PumpLayout(owner);
            var input = workbench.Panels.Composer.Input;
            var presenter = input.GetVisualDescendants().OfType<TextPresenter>().Single();
            var watermark = input.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Name == "PART_Watermark");
            Assert.Equal("输入同传内容", input.Watermark);
            Assert.True(watermark.IsEffectivelyVisible);

            presenter.PreeditText = "pinyin";
            PumpLayout(owner);
            Assert.Equal("", room.InputDraft);
            Assert.False(watermark.IsEffectivelyVisible);

            presenter.PreeditText = null;
            PumpLayout(owner);
            Assert.Equal("", room.InputDraft);
            Assert.True(watermark.IsEffectivelyVisible);

            presenter.PreeditText = "pinyin";
            PumpLayout(owner);
            Assert.False(watermark.IsEffectivelyVisible);
            presenter.PreeditText = null;
            owner.KeyTextInput("拼音");
            PumpLayout(owner);
            Assert.Equal("拼音", room.InputDraft);
            Assert.Equal(2, input.CaretIndex);
            Assert.False(watermark.IsEffectivelyVisible);

            input.Text = "";
            PumpLayout(owner);
            Assert.True(watermark.IsEffectivelyVisible);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

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

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
