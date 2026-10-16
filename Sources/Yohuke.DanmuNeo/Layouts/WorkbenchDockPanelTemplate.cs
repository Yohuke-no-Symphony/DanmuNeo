using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchDockPanelTemplate : IDataTemplate
{
    /// <inheritdoc/>
    public bool Match(object? data)
    {
        return data is WorkbenchDockPanel;
    }

    /// <summary>
    /// 返回工作台持有的面板实例，使布局调整保留控件草稿与业务绑定。
    /// </summary>
    public Control? Build(object? data)
    {
        return data is WorkbenchDockPanel panel ? panel.View : null;
    }
}
