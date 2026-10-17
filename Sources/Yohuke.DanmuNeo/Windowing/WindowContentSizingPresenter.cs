using Avalonia;
using Avalonia.Controls;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class WindowContentSizingPresenter : Decorator
{
    private SizeToContent mode;
    private Size fixedClientSize;
    private Size minClientSize;
    private Size maxClientSize = new(double.PositiveInfinity, double.PositiveInfinity);
    private Thickness clientInsets;

    internal WindowContentSizingPresenter(Control child)
    {
        Child = child;
    }

    internal Size DesiredClientSize { get; private set; }

    internal bool HasMeasured { get; private set; }

    internal void UpdateConstraints(SizeToContent requestedMode, Size requestedClientSize, Size minimumClientSize,
        Size maximumClientSize, Thickness insets)
    {
        if (mode == requestedMode && fixedClientSize == requestedClientSize &&
            minClientSize == minimumClientSize && maxClientSize == maximumClientSize && clientInsets == insets)
        {
            return;
        }

        mode = requestedMode;
        fixedClientSize = requestedClientSize;
        minClientSize = minimumClientSize;
        maxClientSize = maximumClientSize;
        clientInsets = insets;
        HasMeasured = false;
        InvalidateMeasure();
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (mode == SizeToContent.Manual)
        {
            HasMeasured = false;
            return base.MeasureOverride(availableSize);
        }

        var autoWidth = mode is SizeToContent.Width or SizeToContent.WidthAndHeight;
        var autoHeight = mode is SizeToContent.Height or SizeToContent.WidthAndHeight;
        var clientWidth = autoWidth
            ? maxClientSize.Width
            : Math.Clamp(fixedClientSize.Width, minClientSize.Width, maxClientSize.Width);
        var clientHeight = autoHeight
            ? maxClientSize.Height
            : Math.Clamp(fixedClientSize.Height, minClientSize.Height, maxClientSize.Height);
        var horizontalInsets = clientInsets.Left + clientInsets.Right;
        var verticalInsets = clientInsets.Top + clientInsets.Bottom;
        var child = Child!;
        child.Measure(new(Math.Max(0, clientWidth - horizontalInsets),
            Math.Max(0, clientHeight - verticalInsets)));
        var desiredSize = child.DesiredSize;
        DesiredClientSize = new(
            autoWidth
                ? Math.Clamp(desiredSize.Width + horizontalInsets, minClientSize.Width, maxClientSize.Width)
                : clientWidth,
            autoHeight
                ? Math.Clamp(desiredSize.Height + verticalInsets, minClientSize.Height, maxClientSize.Height)
                : clientHeight);
        HasMeasured = true;
        return desiredSize;
    }
}
