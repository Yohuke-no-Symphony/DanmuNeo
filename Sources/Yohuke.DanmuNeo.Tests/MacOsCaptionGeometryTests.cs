using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证 macOS 原生按钮坐标转换及标题行与按钮中线的对齐。
/// </summary>
public sealed class MacOsCaptionGeometryTests
{
    /// <summary>
    /// 翻转与非翻转的原生坐标均转换为客户区左上坐标，并扣除内容原点。
    /// </summary>
    [Theory]
    [InlineData(false, 0, 0, 786)]
    [InlineData(true, 0, 0, 20)]
    [InlineData(false, 50, 100, 886)]
    [InlineData(true, 50, 100, 120)]
    [InlineData(false, -40, -100, 686)]
    [InlineData(true, -40, -100, -80)]
    public void NativeCoordinatesBecomeClientBounds(bool flipped, double originX, double originY, double buttonY)
    {
        var button = new MacOsRect { X = originX + 6, Y = buttonY, Width = 14, Height = 14 };
        var content = new MacOsRect { X = originX, Y = originY, Width = 1280, Height = 820 };

        var bounds = MacOsCaptionGeometry.GetClientBounds(button, content, flipped);

        Assert.Equal(new Rect(6, 20, 14, 14), bounds);
    }

    /// <summary>
    /// 窗口高度变化时，按钮保持距客户区顶部 20 像素，标题行中线仍为 27。
    /// </summary>
    [Theory]
    [InlineData(640, 606)]
    [InlineData(820, 786)]
    [InlineData(1040, 1006)]
    public void ResizingContentPreservesButtonTopAndTitleCenter(double contentHeight, double buttonY)
    {
        var button = new MacOsRect { X = 6, Y = buttonY, Width = 14, Height = 14 };
        var content = new MacOsRect { Width = 1280, Height = contentHeight };

        var bounds = MacOsCaptionGeometry.GetClientBounds(button, content, false);
        var titleHeight = MacOsCaptionGeometry.GetTitleBarHeight(42, bounds);

        Assert.Equal(new Rect(6, 20, 14, 14), bounds);
        Assert.Equal(54, titleHeight);
        Assert.Equal(bounds.Center.Y, titleHeight / 2);
    }

    /// <summary>
    /// 按钮或内容矩形出现非有限坐标、非有限尺寸或非正尺寸时，拒绝传播损坏几何。
    /// </summary>
    [Theory]
    [InlineData(double.NaN, 0, 14, 14)]
    [InlineData(double.PositiveInfinity, 0, 14, 14)]
    [InlineData(0, double.NaN, 14, 14)]
    [InlineData(0, double.NegativeInfinity, 14, 14)]
    [InlineData(0, 0, double.NaN, 14)]
    [InlineData(0, 0, double.PositiveInfinity, 14)]
    [InlineData(0, 0, 0, 14)]
    [InlineData(0, 0, -1, 14)]
    [InlineData(0, 0, 14, double.NaN)]
    [InlineData(0, 0, 14, double.NegativeInfinity)]
    [InlineData(0, 0, 14, 0)]
    [InlineData(0, 0, 14, -1)]
    public void InvalidNativeRectanglesReturnEmptyBounds(double x, double y, double width, double height)
    {
        var invalid = new MacOsRect { X = x, Y = y, Width = width, Height = height };
        var button = new MacOsRect { X = 6, Y = 786, Width = 14, Height = 14 };
        var content = new MacOsRect { Width = 1280, Height = 820 };

        foreach (var flipped in new[] { false, true })
        {
            Assert.Equal(default(Rect), MacOsCaptionGeometry.GetClientBounds(invalid, content, flipped));
            Assert.Equal(default(Rect), MacOsCaptionGeometry.GetClientBounds(button, invalid, flipped));
        }
    }

    /// <summary>
    /// 没有可见原生按钮时保留最小标题高度。
    /// </summary>
    [Fact]
    public void MissingNativeButtonsKeepMinimumTitleHeight()
    {
        var content = new MacOsRect { Width = 1280, Height = 820 };
        var bounds = MacOsCaptionGeometry.GetClientBounds(default, content, false);

        Assert.Equal(default(Rect), bounds);
        Assert.Equal(42, MacOsCaptionGeometry.GetTitleBarHeight(42, bounds));
        Assert.Equal(default(Thickness), MacOsCaptionGeometry.GetCaptionInsets(bounds, 42));
    }

    /// <summary>
    /// 标题高度按按钮中线向上取整，同时保留整数或分数的最小高度。
    /// </summary>
    [Theory]
    [InlineData(42, 20, 14, 54)]
    [InlineData(42, 20.25, 14, 55)]
    [InlineData(42, 20.5, 14, 55)]
    [InlineData(42, 13, 14, 42)]
    [InlineData(42.25, 13, 14, 42.25)]
    [InlineData(53.25, 20, 14, 54)]
    [InlineData(54.25, 20, 14, 54.25)]
    [InlineData(0.5, 0, 0.25, 1)]
    public void TitleHeightUsesButtonCenterWithoutShrinkingMinimum(double minimum, double top, double buttonHeight,
        double expected)
    {
        var bounds = new Rect(6, top, 14, buttonHeight);

        Assert.Equal(expected, MacOsCaptionGeometry.GetTitleBarHeight(minimum, bounds));
    }

    /// <summary>
    /// 最小行高和向上取整产生的剩余高度由底部内距消除，按钮和内容中线保持一致。
    /// </summary>
    [Theory]
    [InlineData(6, 12, 42, 20, 4)]
    [InlineData(6, 20, 42, 20, 0)]
    [InlineData(62, 20.25, 42, 76, 0.5)]
    [InlineData(62, 20.5, 42, 76, 0)]
    [InlineData(-30, 20.25, 42, 0, 0.5)]
    [InlineData(6, 12, 42.5, 20, 4.5)]
    public void CaptionInsetsAlignNativeAndContentCenters(double left, double top, double minimum,
        double expectedLeftInset, double expectedBottomInset)
    {
        var bounds = new Rect(left, top, 14, 14);
        var titleHeight = MacOsCaptionGeometry.GetTitleBarHeight(minimum, bounds);

        var insets = MacOsCaptionGeometry.GetCaptionInsets(bounds, titleHeight);

        Assert.Equal(new Thickness(expectedLeftInset, 0, 0, expectedBottomInset), insets);
        Assert.Equal(bounds.Center.Y, (titleHeight - insets.Bottom) / 2);
    }

    /// <summary>
    /// 无效按钮尺寸和非正或非有限的按钮中线不会改变最小标题高度，也不产生保留区。
    /// </summary>
    [Theory]
    [InlineData(0, 14, 20)]
    [InlineData(-1, 14, 20)]
    [InlineData(double.NaN, 14, 20)]
    [InlineData(double.PositiveInfinity, 14, 20)]
    [InlineData(14, 0, 20)]
    [InlineData(14, -1, 20)]
    [InlineData(14, double.NaN, 20)]
    [InlineData(14, double.PositiveInfinity, 20)]
    [InlineData(14, 14, -7)]
    [InlineData(14, 14, -8)]
    [InlineData(14, 14, double.NaN)]
    [InlineData(14, 14, double.PositiveInfinity)]
    [InlineData(14, 14, double.NegativeInfinity)]
    [InlineData(14, double.MaxValue, double.MaxValue)]
    public void InvalidButtonBoundsKeepMinimumTitleHeight(double width, double height, double top)
    {
        var bounds = new Rect(6, top, width, height);

        Assert.Equal(42.5, MacOsCaptionGeometry.GetTitleBarHeight(42.5, bounds));
        Assert.Equal(default(Thickness), MacOsCaptionGeometry.GetCaptionInsets(bounds, 42.5));
    }

    /// <summary>
    /// 按钮右边界损坏或计算溢出时不传播无效的标题保留区。
    /// </summary>
    [Theory]
    [InlineData(double.NaN, 14)]
    [InlineData(double.PositiveInfinity, 14)]
    [InlineData(double.NegativeInfinity, 14)]
    [InlineData(double.MaxValue, double.MaxValue)]
    public void InvalidButtonRightEdgeReturnsEmptyInsets(double left, double width)
    {
        var bounds = new Rect(left, 12, width, 14);

        Assert.Equal(default(Thickness), MacOsCaptionGeometry.GetCaptionInsets(bounds, 42));
    }

    /// <summary>
    /// 非有限或非正的最小标题高度直接被拒绝。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidMinimumTitleHeightIsRejected(double minimum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MacOsCaptionGeometry.GetTitleBarHeight(minimum, new(6, 20, 14, 14)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MacOsCaptionGeometry.GetTitleBarHeight(minimum, default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MacOsCaptionGeometry.GetCaptionInsets(new(6, 20, 14, 14), minimum));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MacOsCaptionGeometry.GetCaptionInsets(default, minimum));
    }

    /// <summary>
    /// 原生 NSRect 结构保持四个连续 double 且不包含托管引用。
    /// </summary>
    [Fact]
    public void NativeRectUsesUnmanagedThirtyTwoByteLayout()
    {
        Assert.Equal(32, Marshal.SizeOf<MacOsRect>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<MacOsRect>());
    }
}
