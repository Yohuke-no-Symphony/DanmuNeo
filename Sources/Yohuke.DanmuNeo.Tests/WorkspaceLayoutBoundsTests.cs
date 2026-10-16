using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证跨屏幕恢复按物理位置和逻辑尺寸处理浮窗。
/// </summary>
public class WorkspaceLayoutBoundsTests
{
    /// <summary>
    /// 已拔除显示器上的浮窗重新进入现有屏幕的可用区域。
    /// </summary>
    [Fact]
    public void MissingDisplayReturnsFloatingWindowToAnAvailableWorkArea()
    {
        var bounds = WorkspaceLayoutBounds.Clamp(
            new() { X = 5000, Y = 3000, Width = 1200, Height = 800, Scaling = 1 },
            [new(0, 0, 1280, 960, 2)]);

        Assert.Equal(640, bounds.Width);
        Assert.Equal(480, bounds.Height);
        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(2, bounds.Scaling);
    }

    /// <summary>
    /// 仍然存在的左侧屏幕保留浮窗所在屏幕和逻辑尺寸。
    /// </summary>
    [Fact]
    public void ExistingSecondaryScreenRetainsItsWindow()
    {
        var bounds = WorkspaceLayoutBounds.Clamp(
            new() { X = -1600, Y = 100, Width = 600, Height = 400, Scaling = 1 },
            [new(0, 0, 2560, 1440, 2), new(-1920, 0, 1920, 1080, 1)]);

        Assert.Equal(-1600, bounds.X);
        Assert.Equal(100, bounds.Y);
        Assert.Equal(600, bounds.Width);
        Assert.Equal(400, bounds.Height);
        Assert.Equal(1, bounds.Scaling);
    }
}
