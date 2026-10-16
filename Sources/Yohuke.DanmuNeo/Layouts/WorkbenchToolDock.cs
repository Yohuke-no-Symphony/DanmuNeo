using Dock.Controls.DeferredContentControl;
using Dock.Model.Mvvm.Controls;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchToolDock : ToolDock, IDeferredContentPresentation
{
    public bool DeferContentPresentation => false;
}
