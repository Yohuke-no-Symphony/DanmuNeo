using Avalonia.Controls;
using Avalonia.Interactivity;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 独立 SC 与上舰消息面板。
/// </summary>
public partial class SuperChatPanelView : WorkbenchRoomPanel
{
    private readonly WorkbenchFeedInteraction interactions = null!;

    /// <summary>
    /// 创建并连接 SC 面板。
    /// </summary>
    public SuperChatPanelView(WorkbenchContext context, WorkbenchInteractionCoordinator coordinator) : this()
    {
        interactions = new(this, context, coordinator);
        InitializeContext(context);
    }

    /// <summary>创建供 XAML 加载器使用的 SC 面板。</summary>
    public SuperChatPanelView()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
        interactions?.Dispose();
    }

    private async void FeedUserName_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Button { Content: TextBlock { Text: { } name } })
        {
            await interactions.CopyAsync(name);
        }
    }

    private async void SuperChat_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SuperChatItem item } button)
        {
            return;
        }

        if (item.IsGuardPurchase)
        {
            await interactions.CopyAsync(item.UserName);
        }
        else if (item.CanInsertContent)
        {
            interactions.Insert(button, item);
        }
    }
}