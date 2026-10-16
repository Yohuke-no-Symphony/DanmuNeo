using Avalonia.Controls;
using Avalonia.Interactivity;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 独立弹幕列表，保留翻阅锚点和上下文操作。
/// </summary>
public partial class DanmuPanelView : WorkbenchRoomPanel
{
    private readonly DanmuFeedScrollController scrollController;
    private readonly WorkbenchFeedInteraction interactions = null!;

    /// <summary>
    /// 创建并连接弹幕面板。
    /// </summary>
    public DanmuPanelView(WorkbenchContext context, WorkbenchInteractionCoordinator coordinator) : this()
    {
        interactions = new(this, context, coordinator);
        InitializeContext(context);
    }

    /// <summary>创建供 XAML 加载器使用的弹幕面板。</summary>
    public DanmuPanelView()
    {
        InitializeComponent();
        scrollController = new(DanmuScrollViewer, DanmuItemsControl);
    }

    /// <inheritdoc/>
    protected override void OnCurrentRoomChanged(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
        scrollController.Attach(newRoom?.DanmuItems);
    }

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
        scrollController.Dispose();
        interactions?.Dispose();
    }

    internal ContextMenu CreateDanmuContextMenu(DanmuFeedItem item, LiveRoomTabViewModel room)
    {
        return interactions.CreateDanmuContextMenu(item, room);
    }

    private async void FeedUserName_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Button { Content: TextBlock { Text: { } name } })
        {
            await interactions.CopyAsync(name);
        }
    }

    private void Danmu_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DanmuFeedItem item } button)
        {
            interactions.Insert(button, item);
        }
    }

    private void Danmu_OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Button { DataContext: DanmuFeedItem item } button && Context.CurrentRoom is { } room)
        {
            e.Handled = true;
            interactions.CreateDanmuContextMenu(item, room).Open(button);
        }
    }
}