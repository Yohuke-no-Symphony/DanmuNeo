using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>以独立业务上下文验证应用级面板的绑定边界。</summary>
public sealed class ApplicationPanelTestView : WorkbenchPanelView
{
    /// <summary>创建不依赖当前房间的工具面板。</summary>
    public ApplicationPanelTestView(WorkbenchContext context, object viewModel)
    {
        DataContext = viewModel;
        InitializeContext(context);
    }
}
