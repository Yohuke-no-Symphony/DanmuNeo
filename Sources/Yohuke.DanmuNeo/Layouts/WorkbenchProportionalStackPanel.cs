using Yohuke.DanmuNeo.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Dock.Controls.ProportionalStackPanel;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchProportionalStackPanel : ProportionalStackPanel
{
    static WorkbenchProportionalStackPanel()
    {
        AffectsParentMeasure<WorkbenchProportionalStackPanel>(MinHeightProperty);
        AffectsParentArrange<WorkbenchProportionalStackPanel>(MinHeightProperty);
    }

    /// <summary>
    /// 竖向测量在原比例偏好下分配满足内容下限的高度，不回写模型比例。
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Orientation == Orientation.Horizontal)
        {
            return base.MeasureOverride(availableSize);
        }

        if (!double.IsFinite(availableSize.Height))
        {
            throw new InvalidOperationException(Localization.Get("Layout.FiniteHeightRequired"));
        }

        var heights = AllocateHeights(availableSize.Height);
        var width = 0d;

        foreach (var child in Children)
        {
            child.Measure(new(availableSize.Width, heights[child]));

            if (heights[child] > 0)
            {
                width = Math.Max(width, child.DesiredSize.Width);
            }
        }

        return new(width, heights.Values.Sum());
    }

    /// <summary>
    /// 临时最小高度只影响当前排列，恢复空间后自动按保存的偏好重新分配。
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Orientation == Orientation.Horizontal)
        {
            return base.ArrangeOverride(finalSize);
        }

        var heights = AllocateHeights(finalSize.Height);
        var top = 0d;

        foreach (var child in Children)
        {
            var height = heights[child];
            child.Arrange(new(0, top, finalSize.Width, height));
            top += height;
        }

        return finalSize;
    }

    internal static ProportionalStackPanelSplitter? GetSplitter(Control control)
    {
        if (control is ContentPresenter presenter)
        {
            if (presenter.Child is null)
            {
                presenter.UpdateChild();
            }

            return presenter.Child as ProportionalStackPanelSplitter;
        }

        return control as ProportionalStackPanelSplitter;
    }

    private Dictionary<Control, double> AllocateHeights(double height)
    {
        var allocations = Children.ToDictionary(child => child, _ => 0d);
        var pending = new List<Control>();
        var splitterHeight = 0d;

        for (var index = 0; index < Children.Count; index++)
        {
            var child = Children[index];

            if (GetSplitter(child) is { } splitter)
            {
                if (index > 0 && index + 1 < Children.Count &&
                    !GetIsCollapsed(Children[index - 1]) && !GetIsCollapsed(Children[index + 1]))
                {
                    allocations[child] = GetFiniteMinimum(splitter.Thickness);
                    splitterHeight += allocations[child];
                }
            }
            else if (!GetIsCollapsed(child))
            {
                pending.Add(child);
            }
        }

        var weights = pending.ToDictionary(child => child, child => GetProportion(child));
        var unassigned = pending.Where(child => !double.IsFinite(weights[child]) || weights[child] <= 0).ToArray();
        var assignedTotal = pending.Except(unassigned).Sum(child => weights[child]);
        var unassignedWeight = assignedTotal < 1 && unassigned.Length > 0 ? (1 - assignedTotal) / unassigned.Length : 1;

        foreach (var child in unassigned)
        {
            weights[child] = unassignedWeight;
        }

        var minimums = pending.ToDictionary(child => child, child => GetFiniteMinimum(child.MinHeight));
        var remaining = Math.Max(height - splitterHeight, minimums.Values.Sum());

        while (pending.Count > 0)
        {
            var totalWeight = pending.Sum(child => weights[child]);
            var constrained = pending.Where(child => remaining * weights[child] / totalWeight < minimums[child]).ToArray();

            if (constrained.Length == 0)
            {
                foreach (var child in pending)
                {
                    allocations[child] = remaining * weights[child] / totalWeight;
                }

                break;
            }

            foreach (var child in constrained)
            {
                allocations[child] = minimums[child];
                remaining -= minimums[child];
                pending.Remove(child);
            }
        }

        return allocations;
    }

    private static double GetFiniteMinimum(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : 0;
    }
}
