using Yohuke.DanmuNeo.Views.Components;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform;

namespace Yohuke.DanmuNeo.Windowing;

internal static class WindowChrome
{
    internal static WindowTitleBar AttachFrame(Window window, Control? rightContent = null)
    {
        var titleBar = new WindowTitleBar { RightContent = rightContent };
        var titleBinding = titleBar.Bind(WindowTitleBar.TitleProperty,
            new Binding(nameof(Window.Title)) { Source = window });
        var content = window.Content;
        window.Content = null;
        DockPanel.SetDock(titleBar, Avalonia.Controls.Dock.Top);
        window.Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                titleBar,
                new ContentPresenter
                {
                    Content = content,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                }
            }
        };
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        window.ExtendClientAreaTitleBarHeightHint = titleBar.Height;
        var chrome = Attach(window, titleBar);
        window.Closed += OnClosed;
        return titleBar;

        void OnClosed(object? sender, EventArgs e)
        {
            window.Closed -= OnClosed;
            chrome.Dispose();
            titleBinding.Dispose();
        }
    }

    internal static IWindowChrome Attach(Window window, IWindowTitleBar titleBar)
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
