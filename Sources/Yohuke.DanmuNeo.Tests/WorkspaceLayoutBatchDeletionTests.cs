using System.Text.Json;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证布局批量删除在校验全部目标后一次更新预设和当前布局。
/// </summary>
public class WorkspaceLayoutBatchDeletionTests
{
    /// <summary>
    /// 混入内置或不存在的布局时拒绝整批删除，不发出变更或修改持久化状态。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(WorkspaceLayoutPresets.DEFAULT)]
    [InlineData("missing")]
    public async Task InvalidTargetRejectsEntireBatch(string invalidId)
    {
        await using var context = new WorkbenchLayoutTestContext();
        var id = context.Controller.SaveAs("保留的布局");
        await context.Controller.FlushAsync();
        var before = WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile());
        var changes = 0;
        context.Controller.Changed += (_, _) => changes++;

        Assert.False(context.Controller.Delete(new[] { id, invalidId }));

        Assert.Equal(0, changes);
        Assert.Equal(before, WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile()));
        Assert.Equal(before, WorkspaceLayoutSerializer.Serialize(await context.Store.LoadAsync()));
    }

    /// <summary>
    /// 一批删除去重后只发布一次最终状态，保留当前排布且仅回退被删除的预设标识。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchPublishesOneFinalStateAndPersistsRemainingPresets(bool deleteCurrent)
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var retained = context.Controller.SaveAs("保留");
        var first = context.Controller.SaveAs("删除一");
        var second = context.Controller.SaveAs("删除二");
        context.Controller.ApplyPreset(deleteCurrent ? second : retained);
        context.Controller.HidePanel(WorkbenchPanelIds.HISTORY);
        context.Pump();
        var beforeLayout = JsonSerializer.Serialize(context.Controller.Capture());
        var before = WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile());
        var published = new List<WorkspaceLayoutFile>();
        var closed = new List<string>();
        context.Controller.Changed += (_, _) => published.Add(context.Controller.CaptureFile());
        context.Controller.PanelClosedByUser += closed.Add;

        Assert.True(context.Controller.Delete(new[] { first, second, first }));

        var final = Assert.Single(published);
        Assert.Equal(retained, Assert.Single(final.UserPresets).Id);
        Assert.Equal(deleteCurrent ? WorkspaceLayoutPresets.DEFAULT : retained, final.CurrentPresetId);
        Assert.Equal(beforeLayout, JsonSerializer.Serialize(final.CurrentLayout));
        Assert.Contains(WorkbenchPanelIds.HISTORY, final.CurrentLayout.HiddenPanelIds);
        Assert.True(context.Controller.IsModified);
        Assert.Empty(closed);
        Assert.True(WorkspaceLayoutValidator.TryValidate(final, out var error), error);
        Assert.NotEqual(before, WorkspaceLayoutSerializer.Serialize(final));
        context.Pump();
        Assert.Single(published);
        await context.Controller.FlushAsync();
        Assert.Equal(WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile()),
            WorkspaceLayoutSerializer.Serialize(await context.Store.LoadAsync()));
    }

    /// <summary>
    /// 空集合是无副作用的成功操作。
    /// </summary>
    [AvaloniaFact]
    public async Task EmptyBatchDoesNotPublishAChange()
    {
        await using var context = new WorkbenchLayoutTestContext();
        var changes = 0;
        context.Controller.Changed += (_, _) => changes++;

        Assert.True(context.Controller.Delete(Array.Empty<string>()));

        Assert.Equal(0, changes);
        Assert.Equal(4, context.Controller.Presets.Count);
    }
}
