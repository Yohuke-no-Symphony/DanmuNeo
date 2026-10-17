using Yohuke.DanmuNeo.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Yohuke.DanmuNeo.Windowing;

internal static class WindowChrome
{
    internal static IWindowChrome Attach(Window window, TitleBarView titleBar)
    {
        window.SystemDecorations = SystemDecorations.Full;
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" })
        {
            return new WindowsWindowChrome(window, titleBar);
        }

        if (OperatingSystem.IsMacOS() && window.TryGetPlatformHandle() is IPlatformHandle { HandleDescriptor: "NSWindow", Handle: not 0 })
        {
            return new MacOsWindowChrome(window, titleBar);
        }

        return new ManagedWindowChrome(window);
    }

    internal static void ValidateClientSize(Size size)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "客户区尺寸必须是有限正值。");
        }
    }
}
