using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Dock.Controls.ProportionalStackPanel;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchProportionalStackPanelSplitter : ProportionalStackPanelSplitter
{
    private WorkbenchProportionalStackPanel? dragPanel;
    private Point startPoint;
    private Dictionary<Control, double>? originalProportions;
    private Dictionary<Control, double>? visibleProportions;
    private bool baselineApplied;

    protected override Type StyleKeyOverride => typeof(ProportionalStackPanelSplitter);

    /// <summary>
    /// 保存用户按下时的偏好和实际尺寸，单纯按下不改变分栏模型。
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (IsResizingEnabled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            this.GetVisualAncestors().OfType<WorkbenchProportionalStackPanel>().FirstOrDefault() is
                { Orientation: Orientation.Vertical } panel)
        {
            var children = panel.Children.Where(child => !ProportionalStackPanel.GetIsCollapsed(child) &&
                WorkbenchProportionalStackPanel.GetSplitter(child) is null).ToArray();
            var height = children.Sum(child => child.Bounds.Height);

            if (height > 0)
            {
                dragPanel = panel;
                startPoint = e.GetPosition(panel);
                originalProportions = children.ToDictionary(child => child, ProportionalStackPanel.GetProportion);
                visibleProportions = children.ToDictionary(child => child, child => child.Bounds.Height / height);
                baselineApplied = false;
            }
        }

        base.OnPointerPressed(e);
    }

    /// <summary>
    /// 实时拖动首次产生竖向位移后，以当前可见边界交给 Dock 的拖动器计算增量。
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!PreviewResize && HasVerticalMovement(e))
        {
            ApplyVisibleBaseline();
        }

        var restore = !baselineApplied ? originalProportions : null;
        base.OnPointerMoved(e);
        RestoreProportions(restore);
    }

    /// <summary>
    /// 预览拖动在提交时建立实际尺寸基线；没有位移的单击保持原比例。
    /// </summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (PreviewResize && HasVerticalMovement(e))
        {
            ApplyVisibleBaseline();
        }

        var restore = !baselineApplied ? originalProportions : null;
        base.OnPointerReleased(e);
        RestoreProportions(restore);
        ClearDrag();
    }

    /// <summary>
    /// 失去鼠标捕获时释放本次拖动保存的控件引用。
    /// </summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ClearDrag();
    }

    private bool HasVerticalMovement(PointerEventArgs e)
    {
        return dragPanel is not null && Math.Abs(e.GetPosition(dragPanel).Y - startPoint.Y) > 0;
    }

    private void ApplyVisibleBaseline()
    {
        if (baselineApplied || visibleProportions is null)
        {
            return;
        }

        baselineApplied = true;
        RestoreProportions(visibleProportions);
    }

    private static void RestoreProportions(Dictionary<Control, double>? proportions)
    {
        foreach (var pair in proportions ?? [])
        {
            ProportionalStackPanel.SetProportion(pair.Key, pair.Value);
        }
    }

    private void ClearDrag()
    {
        dragPanel = null;
        originalProportions = null;
        visibleProportions = null;
        baselineApplied = false;
    }
}
