using Avalonia;
using Avalonia.Controls;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class WorkbenchWindowTopmost : AvaloniaObject
{
    internal static readonly AttachedProperty<bool> IsIndependentlyTopmostProperty =
        AvaloniaProperty.RegisterAttached<WorkbenchWindowTopmost, Window, bool>("IsIndependentlyTopmost");

    internal static bool GetIsIndependentlyTopmost(Window window)
    {
        return window.GetValue(IsIndependentlyTopmostProperty);
    }

    internal static void SetIsIndependentlyTopmost(Window window, bool value)
    {
        window.SetValue(IsIndependentlyTopmostProperty, value);
    }
}
