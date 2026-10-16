using Avalonia;
using Avalonia.Controls;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 将预设放在左侧、发送配置放在右侧，宽度不足时将配置区整体移到下一行。
/// </summary>
public class ComposerToolbarPanel : Panel
{
    /// <summary>
    /// 同行区域之间的间距。
    /// </summary>
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<ComposerToolbarPanel, double>(nameof(ColumnSpacing), 8);

    /// <summary>
    /// 换行区域之间的间距。
    /// </summary>
    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<ComposerToolbarPanel, double>(nameof(RowSpacing), 4);

    static ComposerToolbarPanel()
    {
        AffectsMeasure<ComposerToolbarPanel>(ColumnSpacingProperty, RowSpacingProperty);
    }

    /// <summary>
    /// 同行区域之间的间距。
    /// </summary>
    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    /// <summary>
    /// 换行区域之间的间距。
    /// </summary>
    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 2)
        {
            return base.MeasureOverride(availableSize);
        }

        var constraint = new Size(availableSize.Width, double.PositiveInfinity);
        var preset = Children[0];
        var actions = Children[1];
        preset.Measure(constraint);
        actions.Measure(constraint);
        var combinedWidth = preset.DesiredSize.Width + ColumnSpacing + actions.DesiredSize.Width;

        if (combinedWidth <= availableSize.Width)
        {
            return new(combinedWidth, Math.Max(preset.DesiredSize.Height, actions.DesiredSize.Height));
        }

        return new(Math.Max(preset.DesiredSize.Width, actions.DesiredSize.Width),
            preset.DesiredSize.Height + RowSpacing + actions.DesiredSize.Height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 2)
        {
            return base.ArrangeOverride(finalSize);
        }

        var preset = Children[0];
        var actions = Children[1];
        var presetSize = preset.DesiredSize;
        var actionsSize = actions.DesiredSize;
        var actionsWidth = Math.Min(finalSize.Width, actionsSize.Width);

        if (presetSize.Width + ColumnSpacing + actionsWidth <= finalSize.Width)
        {
            var rowHeight = Math.Max(presetSize.Height, actionsSize.Height);
            preset.Arrange(new(0, (rowHeight - presetSize.Height) / 2, presetSize.Width, presetSize.Height));
            actions.Arrange(new(finalSize.Width - actionsWidth, (rowHeight - actionsSize.Height) / 2,
                actionsWidth, actionsSize.Height));
        }
        else
        {
            preset.Arrange(new(0, 0, Math.Min(finalSize.Width, presetSize.Width), presetSize.Height));
            actions.Arrange(new(finalSize.Width - actionsWidth, presetSize.Height + RowSpacing,
                actionsWidth, actionsSize.Height));
        }

        return finalSize;
    }
}