using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用真实主窗口和输入焦点验证同传预设快捷键。
/// </summary>
public class MarkGroupShortcutViewTests
{
    /// <summary>
    /// 输入框中的 Tab 只轮切一次，保留焦点、草稿和光标。
    /// </summary>
    [AvaloniaFact]
    public async Task TabInComposerCyclesOnceAndKeepsInput()
    {
        using var context = new MarkGroupCycleTestContext();
        var window = CreateWindow(context);

        try
        {
            await ShowWindowAsync(window);
            var view = window.GetVisualDescendants().OfType<DraftComposerPanelView>().Single();
            var input = view.FindControl<TextBox>("InputDraftTextBox")!;
            input.Text = "正在编辑同传";
            input.Focus();
            input.CaretIndex = 3;
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            await context.ViewModel.CycleWorkspaceMarkGroupCommand.ExecutionTask!;

            Assert.Equal("b", context.ViewModel.SelectedWorkspaceMarkGroup!.Id);
            Assert.True(input.IsFocused);
            Assert.Equal("正在编辑同传", input.Text);
            Assert.Equal(3, input.CaretIndex);
            var selector = view.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => control.Classes.Contains("translateComposerMarkSelector"));
            Assert.Same(context.ViewModel.SelectedWorkspaceMarkGroup, selector.SelectedItem);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 空工作区中的默认 Tab 仍能轮切工作区预设。
    /// </summary>
    [AvaloniaFact]
    public async Task TabWorksWithoutLiveRoom()
    {
        using var context = new MarkGroupCycleTestContext(false);
        var window = CreateWindow(context);

        try
        {
            await ShowWindowAsync(window);
            window.Focus();
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            await context.ViewModel.CycleWorkspaceMarkGroupCommand.ExecutionTask!;

            Assert.Equal("b", context.ViewModel.SelectedWorkspaceMarkGroup!.Id);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 禁用、清空、冲突和 Shift+Tab 保留默认焦点导航，不轮切样式。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("disabled")]
    [InlineData("cleared")]
    [InlineData("conflict")]
    [InlineData("shift")]
    public async Task InactiveTabBindingKeepsFocusNavigation(string mode)
    {
        using var context = new MarkGroupCycleTestContext();
        var binding =
            context.ViewModel.Settings.ShortcutBindings.Single(item =>
                item.ActionKey == ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP);

        switch (mode)
        {
            case "disabled":
                binding.IsEnabled = false;
                break;
            case "cleared":
                binding.GestureText = "";
                binding.IsEnabled = false;
                break;
            case "conflict":
                context.ViewModel.Settings.ShortcutBindings
                    .Single(item => item.ActionKey == ShortcutActionKeys.INPUT_CLEAR_DRAFT).GestureText = "Tab";
                break;
        }

        var window = CreateWindow(context);

        try
        {
            await ShowWindowAsync(window);
            var input = window.GetVisualDescendants().OfType<DraftComposerPanelView>().Single()
                .FindControl<TextBox>("InputDraftTextBox")!;
            input.Focus();
            var modifiers = mode == "shift" ? RawInputModifiers.Shift : RawInputModifiers.None;
            window.KeyPressQwerty(PhysicalKey.Tab, modifiers);
            window.KeyReleaseQwerty(PhysicalKey.Tab, modifiers);

            Assert.Equal("a", context.ViewModel.SelectedWorkspaceMarkGroup!.Id);
            Assert.False(input.IsFocused);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 改绑后的快捷键可在输入框触发，旧 Tab 不再触发轮切。
    /// </summary>
    [AvaloniaFact]
    public async Task CustomGestureCyclesFromTextInput()
    {
        using var context = new MarkGroupCycleTestContext();
        context.ViewModel.Settings.ShortcutBindings
            .Single(item => item.ActionKey == ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP).GestureText = "Ctrl+M";
        var window = CreateWindow(context);

        try
        {
            await ShowWindowAsync(window);
            var input = window.GetVisualDescendants().OfType<DraftComposerPanelView>().Single()
                .FindControl<TextBox>("InputDraftTextBox")!;
            input.Focus();
            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.M, RawInputModifiers.Control);
            await context.ViewModel.CycleWorkspaceMarkGroupCommand.ExecutionTask!;

            Assert.Equal("b", context.ViewModel.SelectedWorkspaceMarkGroup!.Id);
            Assert.True(input.IsFocused);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 快捷键录制时 Tab 只写入绑定，不修改工作区预设。
    /// </summary>
    [AvaloniaFact]
    public async Task RecordingTabDoesNotCycleWorkspace()
    {
        using var context = new MarkGroupCycleTestContext();
        var binding = context.ViewModel.ShortcutBindingGroups.SelectMany(group => group.Bindings)
            .Single(item => item.ActionKey == ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP);
        var view = new ShortcutSettingsView { DataContext = context.ViewModel, Focusable = true };
        var window = CreateWindow(context);
        window.Content = view;

        try
        {
            await ShowWindowAsync(window);
            context.ViewModel.BeginRecordShortcut(binding);
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);

            Assert.False(context.ViewModel.IsRecordingShortcut);
            Assert.Equal("Tab", binding.Binding.GestureText);
            Assert.Equal("a", context.ViewModel.SelectedWorkspaceMarkGroup!.Id);
            await context.ViewModel.SaveAsync();
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    /// <summary>
    /// 连续轮切后立即关闭主窗口，保存仍能完成并保留最终预设及窗口尺寸。
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingAfterRapidCyclesPersistsFinalSelection()
    {
        using var context = new MarkGroupCycleTestContext();
        var window = CreateWindow(context);

        try
        {
            await ShowWindowAsync(window);
            window.Width = 1111;
            var tasks = Enumerable.Range(0, 8).Select(_ => context.ViewModel.CycleWorkspaceMarkGroupAsync()).ToList();
            await window.CloseWorkbenchAsync();
            await Task.WhenAll(tasks);
            Dispatcher.UIThread.RunJobs();
            var loaded = context.StateService.Load();

            Assert.Equal("c", loaded.Workspaces[0].SelectedMarkGroupId);
            Assert.Equal(1111, loaded.Settings.WindowWidth);
        }
        finally
        {
            await CloseWindowAsync(window);
        }
    }

    private static MainWindow CreateWindow(MarkGroupCycleTestContext context)
    {
        return new() { DataContext = context.ViewModel };
    }

    private static async Task ShowWindowAsync(MainWindow window)
    {
        window.Show();
        await window.Initialization;
        Assert.NotNull(window.Workbench);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static async Task CloseWindowAsync(MainWindow window)
    {
        await window.CloseWorkbenchAsync();
        Dispatcher.UIThread.RunJobs();
    }
}
