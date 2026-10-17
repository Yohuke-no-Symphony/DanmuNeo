using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Yohuke.DanmuNeo.I18n;
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
    private string clipboardStatusKey = "";
    private object[] clipboardStatusArguments = [];

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
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Localization.LanguageChanged += OnLanguageChanged;
        RefreshClipboardStatus();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Localization.LanguageChanged -= OnLanguageChanged;
        CancelRuleGesture();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
        isPanelDisposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;

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
                ShowClipboardStatus("UI.ForwardingPanelView.ClipboardUnavailable", true);
                return;
            }

            await clipboard.SetTextAsync(DanmuForwardRuleClipboardService.Serialize(rules.Select(rule => rule.State)));

            if (!isPanelDisposed && Context.IsCurrent(selection))
            {
                ShowClipboardStatus("UI.ForwardingPanelView.RulesCopied", false, rules.Count);
            }
        }
        catch (Exception exception)
        {
            Context.Application.LogErrorOnce(Localization.Format("UI.ForwardingPanelView.LogSource", selection!.RoomId), Localization.Get("UI.ForwardingPanelView.CopyLogFailed"), exception);
            if (!isPanelDisposed && Context.IsCurrent(selection))
            {
                ShowClipboardStatus("UI.ForwardingPanelView.CopyFailed", true, new Func<string>(() => LocalizedExceptionText.Get(exception)));
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
                ShowClipboardStatus("UI.ForwardingPanelView.ClipboardUnavailable", true);
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
                ShowClipboardStatus("UI.ForwardingPanelView.RulesPasted", false, added.Count);
            }
        }
        catch (Exception exception)
        {
            Context.Application.LogErrorOnce(Localization.Format("UI.ForwardingPanelView.LogSource", selection!.RoomId), Localization.Get("UI.ForwardingPanelView.PasteLogFailed"), exception);
            if (!isPanelDisposed && Context.IsCurrent(selection))
            {
                ShowClipboardStatus("UI.ForwardingPanelView.PasteFailed", true, new Func<string>(() => LocalizedExceptionText.Get(exception)));
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

    private void ShowClipboardStatus(string key, bool isError = false, params object[] arguments)
    {
        clipboardStatusKey = key;
        clipboardStatusArguments = arguments;
        RefreshClipboardStatus();
        ForwardClipboardStatus.Classes.Set("error", isError);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshClipboardStatus();
    }

    private void RefreshClipboardStatus()
    {
        ForwardClipboardStatus.Text = clipboardStatusKey.Length == 0
            ? ""
            : Localization.Format(clipboardStatusKey, clipboardStatusArguments.Select(argument =>
                argument is Func<string> provider ? provider() : argument).ToArray());
        ForwardClipboardStatus.IsVisible = clipboardStatusKey.Length > 0;
    }

    private void ForwardUidInput_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is TextBox { InnerRightContent: Border badge } input)
        {
            badge.MaxWidth = Math.Min(MAX_USER_NAME_BADGE_WIDTH, input.Bounds.Width * USER_NAME_BADGE_WIDTH_RATIO);
        }
    }
}
