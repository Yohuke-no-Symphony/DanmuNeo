using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证旧窗口尺寸在首次创建 Dock 布局时的迁移行为。
/// </summary>
public class WorkspaceLayoutMigrationTests
{
    /// <summary>
    /// 无旧配置的首次启动准确使用约定的默认比例。
    /// </summary>
    [Fact]
    public void MissingLegacySettingsCreatesTheExactDefaultLayout()
    {
        var file = WorkspaceLayoutMigration.CreateInitial(null);

        Assert.Equal(WorkspaceLayoutPresets.DEFAULT, file.CurrentPresetId);
        Assert.Equal(new[] { 0.2, 0.5, 0.3 }, file.CurrentLayout.Main!.Children.Select(child => child.Proportion));
        Assert.True(WorkspaceLayoutValidator.TryValidate(file, out var error), error);
    }

    /// <summary>
    /// 有效旧尺寸分别保留侧栏占比和剩余两列的相对宽度。
    /// </summary>
    [Fact]
    public void ExistingDimensionsPreserveSidebarAndContentRatios()
    {
        var file = WorkspaceLayoutMigration.CreateInitial(new()
        {
            SidebarWidth = 300, WindowWidth = 1500,
            WorkspaceLiveColumnWidth = 900, WorkspaceToolColumnWidth = 300
        });
        var columns = file.CurrentLayout.Main!.Children;

        Assert.Equal(0.2, columns[0].Proportion, 10);
        Assert.Equal(0.6, columns[1].Proportion, 10);
        Assert.Equal(0.2, columns[2].Proportion, 10);
        Assert.Equal(new[] { 0.75, 0.25 }, columns[1].Children.Select(child => child.Proportion));
        Assert.True(WorkspaceLayoutValidator.TryValidate(file, out var error), error);
    }

    /// <summary>
    /// 旧侧栏折叠状态迁移为隐藏面板，剩余区域重新归一化。
    /// </summary>
    [Fact]
    public void CollapsedSidebarBecomesHiddenWithoutLeavingAnEmptyColumn()
    {
        var file = WorkspaceLayoutMigration.CreateInitial(new()
        {
            IsSidebarCollapsed = true, SidebarWidth = 300, WindowWidth = 1500,
            WorkspaceLiveColumnWidth = 900, WorkspaceToolColumnWidth = 300
        });
        var layout = file.CurrentLayout;

        Assert.Equal([WorkbenchPanelIds.WORKSPACE], layout.HiddenPanelIds);
        Assert.Equal(new[] { 0.75, 0.25 }, layout.Main!.Children.Select(child => child.Proportion));
        Assert.Equal(WorkbenchPanelIds.LIVE_PLAYER, Assert.Single(layout.Main.Children[0].Children[0].PanelIds));
        Assert.True(WorkspaceLayoutValidator.TryValidate(file, out var error), error);
    }

    /// <summary>
    /// 无效旧侧栏尺寸回退默认侧栏占比，但仍迁移有效内容列比例。
    /// </summary>
    [Theory]
    [InlineData(double.NaN, 1280)]
    [InlineData(-10, 1280)]
    [InlineData(240, double.PositiveInfinity)]
    [InlineData(240, 0)]
    [InlineData(1600, 1280)]
    public void InvalidSidebarDimensionsDoNotDiscardValidContentWidths(double sidebarWidth, double windowWidth)
    {
        var file = WorkspaceLayoutMigration.CreateInitial(new()
        {
            SidebarWidth = sidebarWidth, WindowWidth = windowWidth,
            WorkspaceLiveColumnWidth = 600, WorkspaceToolColumnWidth = 600
        });

        Assert.Equal(new[] { 0.2, 0.4, 0.4 }, file.CurrentLayout.Main!.Children.Select(child => child.Proportion));
        Assert.True(WorkspaceLayoutValidator.TryValidate(file, out var error), error);
    }

    /// <summary>
    /// 缺少任一旧内容列宽时保留有效侧栏占比，内容使用默认相对比例。
    /// </summary>
    [Theory]
    [InlineData(0, 500)]
    [InlineData(500, 0)]
    [InlineData(double.NaN, 500)]
    [InlineData(500, double.PositiveInfinity)]
    public void IncompleteContentWidthsUseTheDefaultRelativeSplit(double liveWidth, double toolWidth)
    {
        var file = WorkspaceLayoutMigration.CreateInitial(new()
        {
            SidebarWidth = 300, WindowWidth = 1200,
            WorkspaceLiveColumnWidth = liveWidth, WorkspaceToolColumnWidth = toolWidth
        });
        var columns = file.CurrentLayout.Main!.Children;

        Assert.Equal(0.25, columns[0].Proportion, 10);
        Assert.Equal(0.75 * 0.625, columns[1].Proportion, 10);
        Assert.Equal(0.75 * 0.375, columns[2].Proportion, 10);
        Assert.True(WorkspaceLayoutValidator.TryValidate(file, out var error), error);
    }

    /// <summary>
    /// 很大的有限列宽不会在归一化时溢出。
    /// </summary>
    [Fact]
    public void LargeFiniteContentWidthsAreNormalizedWithoutOverflow()
    {
        var file = WorkspaceLayoutMigration.CreateInitial(new()
        {
            SidebarWidth = 240, WindowWidth = 1200,
            WorkspaceLiveColumnWidth = double.MaxValue,
            WorkspaceToolColumnWidth = double.MaxValue
        });

        Assert.Equal(new[] { 0.2, 0.4, 0.4 }, file.CurrentLayout.Main!.Children.Select(child => child.Proportion));
        Assert.True(WorkspaceLayoutValidator.TryValidate(file, out var error), error);
    }
}
