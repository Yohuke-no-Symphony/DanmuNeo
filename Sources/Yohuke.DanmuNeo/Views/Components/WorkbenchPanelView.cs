using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>为房间面板与应用级工具提供共同的工作台上下文和释放边界。</summary>
public class WorkbenchPanelView : UserControl, IDisposable
{
    /// <summary>显式工作台上下文的绑定属性。</summary>
    public static readonly DirectProperty<WorkbenchPanelView, WorkbenchContext> ContextProperty =
        AvaloniaProperty.RegisterDirect<WorkbenchPanelView, WorkbenchContext>(nameof(Context), panel => panel.Context);

    private WorkbenchContext context = null!;
    private bool isDisposed;

    /// <summary>未注入工作台的设计时面板不接收业务交互。</summary>
    public WorkbenchPanelView()
    {
        IsEnabled = false;
    }

    /// <summary>由工作台显式注入的应用及选择上下文。</summary>
    public WorkbenchContext Context => context;

    /// <summary>由 Dock 标题栏以当前面板为数据上下文创建的操作模板。</summary>
    public IDataTemplate? HeaderActionsTemplate { get; set; }

    /// <summary>连接工作台，不覆盖面板自己的业务数据上下文。</summary>
    protected void InitializeContext(WorkbenchContext context)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(context);
        if (Context is not null)
        {
            throw new InvalidOperationException(Localization.Get("UI.WorkbenchRoomPanel.ContextAlreadyInitialized"));
        }

        SetAndRaise(ContextProperty, ref this.context, context);
        IsEnabled = true;
        OnContextInitialized();
    }

    /// <summary>连接面板需要的工作台通知。</summary>
    protected virtual void OnContextInitialized()
    {
    }

    /// <summary>在释放平台资源前解除工作台通知。</summary>
    protected virtual void OnContextReleased()
    {
    }

    /// <summary>释放面板自己的事件和平台资源。</summary>
    protected virtual void DisposePanel()
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        OnContextReleased();
        DisposePanel();
        IsEnabled = false;
        GC.SuppressFinalize(this);
    }
}
