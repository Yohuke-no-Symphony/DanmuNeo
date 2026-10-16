using Dock.Avalonia.Controls;
using Dock.Controls.DeferredContentControl;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchToolControl : ToolControl, IDeferredContentPresentation
{
    public bool DeferContentPresentation => false;
    protected override Type StyleKeyOverride => typeof(ToolControl);
}
