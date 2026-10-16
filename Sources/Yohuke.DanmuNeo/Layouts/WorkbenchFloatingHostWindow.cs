using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchFloatingHostWindow : HostWindow, IHostWindow
{
    private readonly Window mainOwner;
    private readonly Action<WorkbenchFloatingHostWindow, bool> closing;
    private readonly Action changed;

    internal WorkbenchFloatingHostWindow(
        Window mainOwner,
        WorkbenchDockFactory factory,
        Action<WorkbenchFloatingHostWindow, bool> closing,
        Action changed)
    {
        this.mainOwner = mainOwner;
        this.closing = closing;
        this.changed = changed;
        MinWidth = 260;
        MinHeight = 180;
        ShowInTaskbar = false;
        DockHost = new() { Factory = factory, InitializeFactory = false, InitializeLayout = false };
        WorkbenchDockTemplateCatalog.Install(DockHost);
        Content = DockHost;
        PositionChanged += (_, _) => changed();
        SizeChanged += (_, _) => changed();
    }

    internal DockControl DockHost { get; }
    internal bool IsInternalClose { get; set; }
    protected override Type StyleKeyOverride => typeof(Window);

    void IHostWindow.Present(bool isDialog)
    {
        if (IsVisible)
        {
            return;
        }

        Window?.Factory?.OnWindowOpened(Window);

        if (isDialog)
        {
            ShowDialog(mainOwner);
        }
        else
        {
            Show(mainOwner);
        }
    }

    /// <summary>
    /// 将 Dock 的根模型绑定到浮窗内容宿主。
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DataContextProperty && DockHost is not null)
        {
            DockHost.Layout = change.NewValue as IRootDock;
        }
    }

    /// <summary>
    /// 用户关闭时先归还隐藏面板，布局重建和退出时只清理宿主。
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var wasTracked = IsTracked;
        IsTracked = false;
        base.OnClosing(e);

        if (e.Cancel)
        {
            IsTracked = wasTracked;
            return;
        }

        if (!IsInternalClose)
        {
            closing(this, wasTracked);
        }

        DockHost.Layout = null;
    }

    /// <summary>
    /// 原生窗口关闭后安排保存完整的工作台状态。
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        changed();
    }
}
