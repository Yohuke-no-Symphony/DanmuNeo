using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 独立转发规则面板，隐藏和迁移不改变转发会话。
/// </summary>
public partial class ForwardingPanelView : WorkbenchRoomPanel
{
    private const double MAX_USER_NAME_BADGE_WIDTH = 100;
    private const double USER_NAME_BADGE_WIDTH_RATIO = 0.4;
    private bool isPanelDisposed;
    private bool isPasting;
    private WorkbenchContext? observedContext;

    /// <summary>
    /// 创建并连接转发面板。
    /// </summary>
    public ForwardingPanelView(WorkbenchContext context) : this()
    {
        InitializeContext(context);
        observedContext = context;
        context.SelectionChanged += Context_OnSelectionChanged;
    }

    /// <summary>
    /// 创建供 XAML 加载器使用的面板实例，运行时通过带上下文的构造入口连接。
    /// </summary>
    public ForwardingPanelView()
    {
        InitializeComponent();
        InitializeRuleInteractions();
    }

    /// <inheritdoc/>
    protected override void OnCurrentRoomChanged(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
        if (oldRoom is not null)
        {
            oldRoom.ForwardRules.CollectionChanged -= ForwardRules_OnCollectionChanged;
        }

        CancelRuleGesture();
        selectedRules.Clear();
        ShowClipboardStatus("");

        if (newRoom is not null)
        {
            newRoom.ForwardRules.CollectionChanged += ForwardRules_OnCollectionChanged;
        }

        QueueSelectionRefresh();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelRuleGesture();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
        isPanelDisposed = true;

        if (observedContext is not null)
        {
            observedContext.SelectionChanged -= Context_OnSelectionChanged;
            observedContext = null;
        }

        DisposeRuleInteractions();
    }

    private void Context_OnSelectionChanged(object? sender, EventArgs e)
    {
        CancelRuleGesture();
        selectedRules.Clear();
        QueueSelectionRefresh();
    }

    private void ForwardDragHandle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DanmuForwardRuleViewModel rule })
        {
            ToggleRuleSelection(rule);
            e.Handled = true;
        }
    }

    private void ForwardDragHandle_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Button && e.Key == Key.A &&
            (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 && Context.CurrentRoom is { } room)
        {
            selectedRules.UnionWith(room.ForwardRules);
            RefreshSelectionVisuals();
            e.Handled = true;
        }
    }

    private void ForwardDragHandle_OnPointerCaptureLost(object? _, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(gesturePointer, e.Pointer))
        {
            CancelRuleGesture();
        }
    }

    private void ForwardRules_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CancelRuleGesture();
        selectedRules.RemoveWhere(rule => Context.CurrentRoom?.ForwardRules.Contains(rule) != true);
        QueueSelectionRefresh();
    }

    private void QueueSelectionRefresh()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!isPanelDisposed)
            {
                RefreshSelectionVisuals();
            }
        }, DispatcherPriority.Background);
    }

    private async void CopyForwardRules_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Context.CurrentRoom is not { } room)
        {
            return;
        }

        var rules = room.ForwardRules.Where(selectedRules.Contains).ToArray();
        await CopyRulesAsync(rules.Length == 0 ? room.ForwardRules.ToArray() : rules);
    }

    private async void CopyForwardRule_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DanmuForwardRuleViewModel rule } &&
            Context.CurrentRoom?.ForwardRules.Contains(rule) == true)
        {
            await CopyRulesAsync([rule]);
        }
    }

    private async Task CopyRulesAsync(IReadOnlyCollection<DanmuForwardRuleViewModel> rules)
    {
        var selection = Context.CaptureSelection();

        if (rules.Count == 0 || !Context.IsCurrent(selection))
        {
            return;
        }

        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            {
                ShowClipboardStatus("当前无法访问剪贴板。", true);
                return;
            }

            await clipboard.SetTextAsync(DanmuForwardRuleClipboardService.Serialize(rules.Select(rule => rule.State)));

            if (!isPanelDisposed && Context.IsCurrent(selection))
            {
                ShowClipboardStatus($"已复制 {rules.Count} 条规则");
            }
        }
        catch (Exception exception)
        {
            Context.Application.LogErrorOnce($"转发规则 / 房间 {selection!.RoomId}", "复制转发规则失败", exception);
            if (!isPanelDisposed && Context.IsCurrent(selection))
            {
                ShowClipboardStatus($"复制失败：{exception.Message}", true);
            }
        }
    }

    private async void PasteForwardRules_OnClick(object? sender, RoutedEventArgs e)
    {
        var selection = Context.CaptureSelection();

        if (isPasting || !Context.IsCurrent(selection))
        {
            return;
        }

        isPasting = true;
        ForwardPasteButton.IsEnabled = false;

        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            {
                ShowClipboardStatus("当前无法访问剪贴板。", true);
                return;
            }

            var text = await clipboard.TryGetTextAsync();

            if (isPanelDisposed || !Context.IsCurrent(selection))
            {
                return;
            }

            var states = DanmuForwardRuleClipboardService.Deserialize(text ?? "");
            var added = await selection!.Room.AddForwardRuleStatesAsync(states);

            if (!isPanelDisposed && Context.IsCurrent(selection))
            {
                ShowClipboardStatus($"已粘贴 {added.Count} 条规则，默认未启用");
            }
        }
        catch (Exception exception)
        {
            Context.Application.LogErrorOnce($"转发规则 / 房间 {selection!.RoomId}", "粘贴转发规则失败", exception);
            if (!isPanelDisposed && Context.IsCurrent(selection))
            {
                ShowClipboardStatus($"粘贴失败：{exception.Message}", true);
            }
        }
        finally
        {
            isPasting = false;

            if (!isPanelDisposed)
            {
                ForwardPasteButton.IsEnabled = true;
            }
        }
    }

    private void ShowClipboardStatus(string text, bool isError = false)
    {
        ForwardClipboardStatus.Text = text;
        ForwardClipboardStatus.IsVisible = text.Length > 0;
        ForwardClipboardStatus.Classes.Set("error", isError);
    }

    private void ForwardUidInput_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is TextBox { InnerRightContent: Border badge } input)
        {
            badge.MaxWidth = Math.Min(MAX_USER_NAME_BADGE_WIDTH, input.Bounds.Width * USER_NAME_BADGE_WIDTH_RATIO);
        }
    }
}
