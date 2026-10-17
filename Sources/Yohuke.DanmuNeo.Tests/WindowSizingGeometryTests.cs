using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证不同平台内容尺寸比较使用一致的物理像素换算。
/// </summary>
public sealed class WindowSizingGeometryTests
{
    /// <summary>
    /// 原生整数像素换回逻辑尺寸后，分数缩放不额外增加像素。
    /// </summary>
    [Theory]
    [InlineData(1.25, 83)]
    [InlineData(1.3, 75)]
    [InlineData(1.75, 9)]
    [InlineData(2.0, 249)]
    public void MeasuredPixelSizeRemainsStableAfterLogicalRoundTrip(double scaling, int pixels)
    {
        Assert.Equal(pixels, WindowSizingGeometry.ToPixels(pixels / scaling, scaling));
    }

    /// <summary>
    /// 实际超出整数像素的内容尺寸仍向上取整，避免裁剪。
    /// </summary>
    [Fact]
    public void RealFractionalPixelStillRoundsUp()
    {
        Assert.Equal(126, WindowSizingGeometry.ToPixels(100.01, 1.25));
    }

    /// <summary>
    /// 无效缩放在进入原生尺寸比较之前被拒绝。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidScalingIsRejected(double scaling)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowSizingGeometry.ToPixels(100, scaling));
    }
}
