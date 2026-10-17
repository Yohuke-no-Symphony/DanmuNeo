using System.Runtime.InteropServices;

namespace Yohuke.DanmuNeo.Windowing;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsMargins
{
    internal int LeftWidth { get; set; }
    internal int RightWidth { get; set; }
    internal int TopHeight { get; set; }
    internal int BottomHeight { get; set; }
}
