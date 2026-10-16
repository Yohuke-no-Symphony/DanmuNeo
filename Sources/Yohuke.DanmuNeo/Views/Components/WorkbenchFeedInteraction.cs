using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 弹幕和 SC 共用的剪贴板、插入和上下文菜单行为。
/// </summary>
internal sealed class WorkbenchFeedInteraction : IDisposable
{
    private readonly Control owner;
    private readonly WorkbenchContext context;
    private readonly WorkbenchInteractionCoordinator interactions;
    private readonly ConditionalWeakTable<Button, WorkbenchFeedActivation> activations = new();
    private bool isDisposed;

    /// <summary>连接当前面板与工作台的共享交互。</summary>
    public WorkbenchFeedInteraction(Control owner, WorkbenchContext context,
        WorkbenchInteractionCoordinator interactions)
    {
        this.owner = owner;
        this.context = context;
        this.interactions = interactions;
        owner.AddHandler(InputElement.PointerPressedEvent, Owner_OnPointerPressed, RoutingStrategies.Tunnel, true);
        owner.AddHandler(InputElement.KeyDownEvent, Owner_OnKeyDown, RoutingStrategies.Tunnel, true);
    }

    /// <summary>校验弹幕来源后，将内容插入所属房间的输入面板。</summary>
    public void Insert(Button button, DanmuFeedItem item)
    {
        Insert(button, item, item.Content);
    }

    /// <summary>校验 SC 来源后，将内容插入所属房间的输入面板。</summary>
    public void Insert(Button button, SuperChatItem item)
    {
        Insert(button, item, item.Content);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        isDisposed = true;
        owner.RemoveHandler(InputElement.PointerPressedEvent, Owner_OnPointerPressed);
        owner.RemoveHandler(InputElement.KeyDownEvent, Owner_OnKeyDown);
        activations.Clear();
    }

    /// <summary>复制内容到当前宿主的剪贴板。</summary>
    public async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(owner)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <summary>创建绑定到消息来源房间的弹幕菜单。</summary>
    public ContextMenu CreateDanmuContextMenu(DanmuFeedItem item, LiveRoomTabViewModel sourceRoom)
    {
        var selection = context.CaptureSelection();
        var copyText = new MenuItem { Header = "复制文本" };
        copyText.Click += async (_, _) => await CopyAsync(item.Content);
        var copyUid = new MenuItem
        {
            Header = "复制用户 UID", IsEnabled = !string.IsNullOrWhiteSpace(item.UserUid)
        };
        copyUid.Click += async (_, _) => await CopyAsync(item.UserUid);
        var forward = new MenuItem
        {
            Header = "引入到工作区转发",
            IsEnabled = !string.IsNullOrWhiteSpace(item.UserUid),
            ItemsSource = context.Application.Workspaces.Select(workspace => new MenuItem
            {
                Header = workspace.Name,
                IsEnabled = workspace.Rooms.Any(room => room.Id != sourceRoom.Id),
                ItemsSource = workspace.Rooms.Where(room => room.Id != sourceRoom.Id)
                    .Select(room => CreateForwardTarget(item, sourceRoom, room, selection)).ToList()
            }).ToList()
        };
        var mute = new MenuItem
        {
            Header = "检查禁言权限…", Command = sourceRoom.MuteDanmuCommand,
            CommandParameter = item, IsEnabled = false
        };
        var menu = new ContextMenu
        {
            ItemsSource = new object[] { copyText, copyUid, new Separator(), forward, new Separator(), mute }
        };
        menu.Opened += async (_, _) => await RefreshMuteMenuItemAsync(menu, mute, item, sourceRoom);
        return menu;
    }

    private void Insert(Button button, object item, string content)
    {
        if (isDisposed || !ReferenceEquals(button.DataContext, item))
        {
            return;
        }

        if (!activations.TryGetValue(button, out var activation))
        {
            activation = CreateActivation(item);
        }

        if (activation is not null && ReferenceEquals(activation.Item, item) &&
            context.IsCurrent(activation.Selection) && OwnsItem(activation.Selection.Room, item))
        {
            interactions.InsertFeedContent(activation.Selection.Room, activation.Selection, content);
        }
    }

    private void Owner_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CaptureButtonActivation(e.Source);
    }

    private void Owner_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            CaptureButtonActivation(e.Source);
        }
    }

    private void CaptureButtonActivation(object? source)
    {
        if (source is not Control control)
        {
            return;
        }

        var button = control as Button;

        if (button is null || !IsFeedButton(button))
        {
            button = control.GetVisualAncestors().OfType<Button>().FirstOrDefault(IsFeedButton);
        }

        if (button?.DataContext is { } item && CreateActivation(item) is { } activation)
        {
            activations.Remove(button);
            activations.Add(button, activation);
        }
    }

    private WorkbenchFeedActivation? CreateActivation(object item)
    {
        return context.CaptureSelection() is { } selection && OwnsItem(selection.Room, item)
            ? new(selection, item)
            : null;
    }

    private static bool IsFeedButton(Button button)
    {
        return button.Classes.Contains("danmuFeedButton") || button.Classes.Contains("scFeedButton");
    }

    private static bool OwnsItem(LiveRoomTabViewModel room, object item)
    {
        return item switch
        {
            DanmuFeedItem danmu => room.DanmuItems.Contains(danmu),
            SuperChatItem superChat => room.SuperChats.Contains(superChat),
            _ => false
        };
    }

    private MenuItem CreateForwardTarget(DanmuFeedItem item, LiveRoomTabViewModel sourceRoom,
        LiveRoomTabViewModel targetRoom, WorkbenchSelection? selection)
    {
        var menuItem = new MenuItem { Header = targetRoom.Header };
        menuItem.Click += async (_, _) =>
        {
            if (context.IsCurrent(selection) && ReferenceEquals(selection!.Room, sourceRoom))
            {
                await context.Application.AddDanmuForwardRuleFromFeedAsync(sourceRoom, item, targetRoom);
            }
        };
        return menuItem;
    }

    private static async Task RefreshMuteMenuItemAsync(ContextMenu menu, MenuItem mute,
        DanmuFeedItem item, LiveRoomTabViewModel room)
    {
        using var cancellation = new CancellationTokenSource();
        EventHandler<RoutedEventArgs> closed = (_, _) => cancellation.Cancel();
        menu.Closed += closed;

        try
        {
            var allowed = await room.RefreshModerationPermissionAsync(item, cancellation.Token);

            if (!cancellation.IsCancellationRequested && menu.IsOpen)
            {
                mute.Header = allowed ? "禁言该用户" : "无法禁言该用户";
                mute.IsEnabled = allowed;
                ToolTip.SetTip(mute, room.ModerationStatus);
            }
        }
        finally
        {
            menu.Closed -= closed;
        }
    }
}