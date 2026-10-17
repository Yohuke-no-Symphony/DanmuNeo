using Avalonia;
using Avalonia.Controls;

namespace Yohuke.DanmuNeo.Windowing;

internal interface IWindowTitleBar
{
    Control View { get; }
    Thickness CaptionInsets { get; set; }
    bool IsDragRegion(Point windowPoint);
}
