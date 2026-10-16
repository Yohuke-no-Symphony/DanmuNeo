using Dock.Controls.DeferredContentControl;
using Dock.Model.Mvvm.Controls;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchProportionalDock : ProportionalDock, IDeferredContentPresentation
{
    public bool DeferContentPresentation => false;
}
