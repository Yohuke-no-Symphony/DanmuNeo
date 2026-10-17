using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 组合独立业务面板的组件宿主，供不使用 Dock 的嵌入及组件验证复用。
/// </summary>
public partial class LiveRoomWorkspaceView : UserControl, IDisposable
{
    private WorkbenchContext? context;
    private WorkbenchShortcutRouter? shortcuts;
    private Window? attachedWindow;
    private TabControl? tools;
    private bool isDisposed;
    private readonly List<IDisposable> localizedTabs = new();

    /// <summary>
    /// 创建等待显式上下文注入的宿主。
    /// </summary>
    public LiveRoomWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LiveRoomTabViewModel room)
            {
                context?.SetPreviewRoom(room);
            }
        };
    }

    /// <summary>
    /// 组合共享上下文中的七个独立面板。
    /// </summary>
    public LiveRoomWorkspaceView(WorkbenchContext context) : this()
    {
        this.context = context;
        DataContext = context.CurrentRoom;
        WorkbenchPanels = new(context);
        WorkbenchPanels.SetPanelActivator(ActivatePanel);
        var live = new Grid { RowDefinitions = new("*,Auto"), RowSpacing = 12 };
        live.Children.Add(WorkbenchPanels.Player);
        live.Children.Add(WorkbenchPanels.Composer);
        Grid.SetRow(WorkbenchPanels.Composer, 1);
        WorkspaceColumns.Children.Add(new Border { Classes = { "panel" }, Padding = new(14), Child = live });
        WorkspaceColumns.ColumnDefinitions[0].MinWidth = 420;
        WorkspaceColumns.ColumnDefinitions[2].MinWidth = 300;
        var splitter = new GridSplitter
        {
            Width = 2, ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext
        };
        Grid.SetColumn(splitter, 1);
        WorkspaceColumns.Children.Add(splitter);
        var feed = new Grid { RowDefinitions = new("35*,4,65*") };
        feed.Children.Add(WorkbenchPanels.SuperChat);
        feed.Children.Add(WorkbenchPanels.Danmu);
        Grid.SetRow(WorkbenchPanels.Danmu, 2);
        var feedSplitter = new GridSplitter
        {
            Height = 4, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext
        };
        Grid.SetRow(feedSplitter, 1);
        feed.Children.Add(feedSplitter);
        tools = new()
        {
            Classes = { "workspaceToolTabs" },
            Items =
            {
                CreateToolTab("UI.LiveRoomWorkspaceView.DanmuTab", feed),
                CreateToolTab("UI.LiveRoomWorkspaceView.HistoryTab", WorkbenchPanels.History),
                CreateToolTab("UI.LiveRoomWorkspaceView.LyricsTab", WorkbenchPanels.Lyrics),
                CreateToolTab("UI.LiveRoomWorkspaceView.ForwardingTab", WorkbenchPanels.Forwarding)
            }
        };
        var toolBorder = new Border { Classes = { "panel" }, Padding = new(14), Child = tools };
        Grid.SetColumn(toolBorder, 2);
        WorkspaceColumns.Children.Add(toolBorder);
        var names = new NameScope();
        names.Register("WorkspaceColumns", WorkspaceColumns);
        RegisterNames(names, WorkbenchPanels.Composer,
            "InputDraftTextBox", "ComposerMarkSelector", "DraftSendErrorText", "DanmuColorSelector", "DanmuModeSelector");
        RegisterNames(names, WorkbenchPanels.Danmu, "DanmuScrollViewer", "DanmuItemsControl");
        RegisterNames(names, WorkbenchPanels.Lyrics, "LyricListBox", "NextLyricButton", "LyricTitleButton");
        NameScope.SetNameScope(this, names);
        shortcuts = new(context, WorkbenchPanels, ActivatePanel, context.Application.OpenSettings, () => false);
    }

    /// <summary>
    /// 被组合的唯一业务面板实例。
    /// </summary>
    public WorkbenchPanelSet? WorkbenchPanels { get; }

    /// <summary>
    /// 聚焦共享输入面板。
    /// </summary>
    public void FocusInputDraftFromShortcut()
    {
        WorkbenchPanels?.FocusInputDraft();
    }

    /// <summary>
    /// 清空共享输入面板草稿。
    /// </summary>
    public void ClearInputDraftFromShortcut()
    {
        WorkbenchPanels?.ClearInputDraft();
    }

    /// <summary>
    /// 跳到下一句歌词并恢复滚动跟随。
    /// </summary>
    public void SeekNextLyricFromShortcut()
    {
        WorkbenchPanels?.Lyrics.SeekNextLyricFromShortcut();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attachedWindow = TopLevel.GetTopLevel(this) as Window;

        if (attachedWindow is not null)
        {
            shortcuts?.Attach(attachedWindow);
            attachedWindow.Closed += Window_OnClosed;
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        WorkbenchPanels?.StopHeldActions();

        if (attachedWindow is not null)
        {
            shortcuts?.Detach(attachedWindow);
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        if (attachedWindow is not null)
        {
            attachedWindow.Closed -= Window_OnClosed;
        }

        shortcuts?.Dispose();
        foreach (var binding in localizedTabs)
        {
            binding.Dispose();
        }

        localizedTabs.Clear();
        WorkbenchPanels?.Dispose();
        context?.Dispose();
    }

    internal ContextMenu CreateDanmuContextMenu(DanmuFeedItem item, LiveRoomTabViewModel room)
    {
        return WorkbenchPanels!.Danmu.CreateDanmuContextMenu(item, room);
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        Dispose();
    }

    private void ActivatePanel(string panelId)
    {
        if (tools is not null)
        {
            tools.SelectedIndex = panelId switch
            {
                WorkbenchPanelIds.HISTORY => 1,
                WorkbenchPanelIds.LYRICS => 2,
                WorkbenchPanelIds.FORWARDING => 3,
                WorkbenchPanelIds.DANMU or WorkbenchPanelIds.SUPER_CHAT => 0,
                _ => tools.SelectedIndex
            };
        }
    }

    private static void RegisterNames(NameScope names, Control panel, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            names.Register(alias, panel.FindControl<Control>(alias)!);
        }
    }

    private TabItem CreateToolTab(string key, Control content)
    {
        var tab = new TabItem { Content = content };
        localizedTabs.Add(tab.Bind(TabItem.HeaderProperty, Localization.Observe(key)));
        return tab;
    }
}
