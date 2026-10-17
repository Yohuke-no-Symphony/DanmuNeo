using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证原生窗口消息结构体的内存布局与非托管约束。
/// </summary>
public sealed class WindowsChromeInteropLayoutTests
{
    /// <summary>
    /// 结构体大小与 Windows SDK 的布局保持一致。
    /// </summary>
    [Fact]
    public void NativeValueTypesMatchWindowsSdkStructureSizes()
    {
        Assert.Equal(8, Marshal.SizeOf<WindowsPoint>());
        Assert.Equal(16, Marshal.SizeOf<WindowsRect>());
        Assert.Equal(16, Marshal.SizeOf<WindowsMargins>());
        Assert.Equal(40, Marshal.SizeOf<WindowsMinMaxInfo>());
        Assert.Equal(44, Marshal.SizeOf<WindowsWindowPlacement>());
        Assert.Equal(140, Marshal.SizeOf<WindowsTitleBarInfoEx>());
        Assert.Equal(48 + IntPtr.Size, Marshal.SizeOf<WindowsNccalcSizeParameters>());
    }

    /// <summary>
    /// 窗口消息参数不包含托管引用。
    /// </summary>
    [Fact]
    public void HookParametersContainNoManagedReferences()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<WindowsRect>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<WindowsMinMaxInfo>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<WindowsNccalcSizeParameters>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<WindowsWindowPlacement>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<WindowsTitleBarInfoEx>());
    }
}
