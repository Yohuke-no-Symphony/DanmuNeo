using Avalonia.Input;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证样式轮切、保存重载和新增快捷键的兼容配置。
/// </summary>
public class MarkGroupCycleTests
{
    /// <summary>
    /// 轮切按显示顺序回绕，并只保存当前工作区的选择。
    /// </summary>
    [Fact]
    public async Task CycleWrapsAndPersistsCurrentWorkspaceOnly()
    {
        using var context = new MarkGroupCycleTestContext();
        var viewModel = context.ViewModel;

        await viewModel.CycleWorkspaceMarkGroupAsync();
        Assert.Equal("b", viewModel.SelectedWorkspaceMarkGroup!.Id);
        await viewModel.CycleWorkspaceMarkGroupAsync();
        Assert.Equal("c", viewModel.SelectedWorkspaceMarkGroup.Id);
        await viewModel.CycleWorkspaceMarkGroupAsync();
        Assert.Equal("a", viewModel.SelectedWorkspaceMarkGroup.Id);
        await viewModel.CycleWorkspaceMarkGroupAsync();
        var loaded = context.StateService.Load();

        Assert.Equal("b", loaded.Workspaces[0].SelectedMarkGroupId);
        Assert.Equal("c", loaded.Workspaces[1].SelectedMarkGroupId);
    }

    /// <summary>
    /// 快速连续轮切的每次选择即时更新，写盘顺序保持一致。
    /// </summary>
    [Fact]
    public async Task RapidCyclesRetainEveryTransition()
    {
        using var context = new MarkGroupCycleTestContext();
        var tasks = Enumerable.Range(0, 8).Select(_ => context.ViewModel.CycleWorkspaceMarkGroupAsync()).ToList();
        Assert.Equal("c", context.ViewModel.SelectedWorkspaceMarkGroup!.Id);
        await Task.WhenAll(tasks);

        Assert.Equal("c", context.StateService.Load().Workspaces[0].SelectedMarkGroupId);
    }

    /// <summary>
    /// 排队保存期间切换工作区，两边的预设选择均能重载。
    /// </summary>
    [Fact]
    public async Task WorkspaceSwitchDuringQueuedCyclesPersistsBothSelections()
    {
        using var context = new MarkGroupCycleTestContext();
        var viewModel = context.ViewModel;
        var tasks = Enumerable.Range(0, 4).Select(_ => viewModel.CycleWorkspaceMarkGroupAsync()).ToList();
        viewModel.SelectedWorkspace = viewModel.Workspaces[1];
        tasks.Add(viewModel.CycleWorkspaceMarkGroupAsync());
        Assert.Equal("a", viewModel.SelectedWorkspaceMarkGroup!.Id);
        await Task.WhenAll(tasks);
        var loaded = context.StateService.Load();

        Assert.Equal("b", loaded.Workspaces[0].SelectedMarkGroupId);
        Assert.Equal("a", loaded.Workspaces[1].SelectedMarkGroupId);
    }

    /// <summary>
    /// 无直播间的工作区可轮切；单个、空集合和未选工作区安全保持原状态。
    /// </summary>
    [Fact]
    public async Task EmptyRoomAndMissingSelectionAreHandled()
    {
        using var context = new MarkGroupCycleTestContext(false);
        var viewModel = context.ViewModel;
        await viewModel.CycleWorkspaceMarkGroupAsync();
        Assert.Equal("b", viewModel.SelectedWorkspaceMarkGroup!.Id);
        viewModel.SymbolGroups.Clear();
        await viewModel.CycleWorkspaceMarkGroupAsync();
        Assert.Equal("b", viewModel.SelectedWorkspace!.SelectedMarkGroupId);
        viewModel.SymbolGroups.Add(new() { Id = "b" });
        await viewModel.CycleWorkspaceMarkGroupAsync();
        Assert.Equal("b", viewModel.SelectedWorkspace.SelectedMarkGroupId);
        viewModel.SelectedWorkspace = null;
        await viewModel.CycleWorkspaceMarkGroupAsync();
    }

    /// <summary>
    /// 旧配置补上默认 Tab，明确改绑或禁用仍然保留。
    /// </summary>
    [Fact]
    public void ExistingSettingsReceiveTabAndKeepCustomBinding()
    {
        var settings = new AppSettings();
        ShortcutBindingService.Normalize(settings);
        var binding = Assert.Single(settings.ShortcutBindings,
            item => item.ActionKey == ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP);
        Assert.Equal("Tab", binding.GestureText);
        Assert.Equal(ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP,
            ShortcutBindingService.GetTriggeredActionKey(settings.ShortcutBindings, Key.Tab, KeyModifiers.None));
        binding.GestureText = "Ctrl+M";
        binding.IsEnabled = false;
        ShortcutBindingService.Normalize(settings);

        Assert.Equal("Ctrl+M", binding.GestureText);
        Assert.False(binding.IsEnabled);
    }
}
