using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证内置布局的业务分组和实例隔离。
/// </summary>
public class WorkspaceLayoutPresetsTests
{
    /// <summary>
    /// 四个内置布局覆盖全部面板，并保持约定的区域比例。
    /// </summary>
    [Theory]
    [InlineData(WorkspaceLayoutPresets.DEFAULT, WorkbenchPanelIds.LIVE_PLAYER)]
    [InlineData(WorkspaceLayoutPresets.TRANSLATION, WorkbenchPanelIds.LIVE_PLAYER)]
    [InlineData(WorkspaceLayoutPresets.LYRICS, WorkbenchPanelIds.LYRICS)]
    [InlineData(WorkspaceLayoutPresets.FORWARDING, WorkbenchPanelIds.FORWARDING)]
    public void BuiltInLayoutsKeepWorkspaceAndComposerInTheirAssignedRegions(string presetId, string mainPanelId)
    {
        var layout = WorkspaceLayoutPresets.Create(presetId);

        Assert.True(WorkspaceLayoutValidator.TryValidate(layout, out var error), error);
        var root = Assert.IsType<WorkspaceLayoutNode>(layout.Main);
        Assert.Equal(WorkspaceLayoutNodeKind.Split, root.Kind);
        Assert.Equal(WorkspaceLayoutOrientation.Horizontal, root.Orientation);
        Assert.Equal(new[] { 0.2, 0.5, 0.3 }, root.Children.Select(child => child.Proportion));
        Assert.Equal([WorkbenchPanelIds.WORKSPACE], root.Children[0].PanelIds);
        var center = root.Children[1];
        Assert.Equal(WorkspaceLayoutOrientation.Vertical, center.Orientation);
        Assert.Equal(new[] { 0.75, 0.25 }, center.Children.Select(child => child.Proportion));
        Assert.Equal([mainPanelId], center.Children[0].PanelIds);
        Assert.Equal([WorkbenchPanelIds.COMPOSER], center.Children[1].PanelIds);
        Assert.Equal(WorkbenchPanelIds.COMPOSER, layout.FocusedPanelId);
        Assert.Empty(layout.FloatingWindows);
    }

    /// <summary>
    /// 默认布局保留独立 SC 和含其余信息视图的标签组。
    /// </summary>
    [Fact]
    public void DefaultLayoutGroupsSecondaryViewsBelowSuperChat()
    {
        var layout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.DEFAULT);
        var right = layout.Main!.Children[2];

        Assert.Equal(WorkspaceLayoutOrientation.Vertical, right.Orientation);
        Assert.Equal(new[] { 0.3, 0.7 }, right.Children.Select(child => child.Proportion));
        Assert.Equal([WorkbenchPanelIds.SUPER_CHAT], right.Children[0].PanelIds);
        Assert.Equal(new[]
        {
            WorkbenchPanelIds.DANMU, WorkbenchPanelIds.HISTORY,
            WorkbenchPanelIds.LYRICS, WorkbenchPanelIds.FORWARDING, WorkbenchPanelIds.LOG
        }, right.Children[1].PanelIds);
        Assert.Equal(WorkbenchPanelIds.DANMU, right.Children[1].ActivePanelId);
        Assert.Empty(layout.HiddenPanelIds);
    }

    /// <summary>
    /// 同传布局独立显示 SC、弹幕与历史，同时隐藏歌词和转发。
    /// </summary>
    [Fact]
    public void TranslationLayoutSeparatesLiveFeedsAndHidesUnrelatedTools()
    {
        var layout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.TRANSLATION);
        var right = layout.Main!.Children[2];

        Assert.Equal(new[] { 0.25, 0.5, 0.25 }, right.Children.Select(child => child.Proportion));
        Assert.Equal(new[]
        {
            WorkbenchPanelIds.SUPER_CHAT, WorkbenchPanelIds.DANMU, WorkbenchPanelIds.HISTORY
        }, right.Children.Select(child => child.PanelIds[0]));
        Assert.Equal([WorkbenchPanelIds.HISTORY, WorkbenchPanelIds.LOG], right.Children[2].PanelIds);
        Assert.Equal(new[] { WorkbenchPanelIds.LYRICS, WorkbenchPanelIds.FORWARDING }, layout.HiddenPanelIds);
    }

    /// <summary>
    /// 歌词和转发布局把播放器放入右上区，右下标签不重复主工具。
    /// </summary>
    [Theory]
    [InlineData(WorkspaceLayoutPresets.LYRICS, WorkbenchPanelIds.FORWARDING)]
    [InlineData(WorkspaceLayoutPresets.FORWARDING, WorkbenchPanelIds.LYRICS)]
    public void FocusedToolLayoutsMovePlayerAndKeepRemainingTabs(string presetId, string secondaryToolId)
    {
        var layout = WorkspaceLayoutPresets.Create(presetId);
        var right = layout.Main!.Children[2];

        Assert.Equal(new[] { 0.45, 0.55 }, right.Children.Select(child => child.Proportion));
        Assert.Equal([WorkbenchPanelIds.LIVE_PLAYER], right.Children[0].PanelIds);
        Assert.Equal(new[]
        {
            WorkbenchPanelIds.DANMU, WorkbenchPanelIds.SUPER_CHAT,
            WorkbenchPanelIds.HISTORY, secondaryToolId, WorkbenchPanelIds.LOG
        }, right.Children[1].PanelIds);
        Assert.Equal(WorkbenchPanelIds.DANMU, right.Children[1].ActivePanelId);
        Assert.Empty(layout.HiddenPanelIds);
    }

    /// <summary>
    /// 调整返回布局不能污染内置定义或下一次创建的默认文件。
    /// </summary>
    [Fact]
    public void BuiltInLayoutsAndDefaultFilesNeverShareMutableCollections()
    {
        var first = WorkspaceLayoutPresets.BuiltIn;
        var second = WorkspaceLayoutPresets.BuiltIn;
        var defaultFile = WorkspaceLayoutPresets.CreateDefaultFile();
        first[0].Layout.Main!.Children[0].PanelIds.Clear();
        first[0].Layout.HiddenPanelIds.Add(WorkbenchPanelIds.WORKSPACE);
        defaultFile.CurrentLayout.Main!.Children.Clear();

        Assert.Equal(4, second.Count);
        Assert.Equal(new[]
        {
            WorkspaceLayoutPresets.DEFAULT, WorkspaceLayoutPresets.TRANSLATION,
            WorkspaceLayoutPresets.LYRICS, WorkspaceLayoutPresets.FORWARDING
        }, second.Select(preset => preset.Id));
        Assert.All(second, preset => Assert.True(WorkspaceLayoutValidator.TryValidate(preset.Layout, out var error), error));
        var nextFile = WorkspaceLayoutPresets.CreateDefaultFile();
        Assert.Equal(WorkspaceLayoutFile.CURRENT_VERSION, nextFile.Version);
        Assert.Equal(WorkspaceLayoutPresets.DEFAULT, nextFile.CurrentPresetId);
        Assert.Empty(nextFile.UserPresets);
        Assert.True(WorkspaceLayoutValidator.TryValidate(nextFile, out var validationError), validationError);
    }

    /// <summary>
    /// 未知预设不会被悄悄替换为默认布局。
    /// </summary>
    [Fact]
    public void UnknownPresetIsRejected()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceLayoutPresets.Create("missing"));
    }
}
