using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

public partial class ForwardingPanelView
{
    private const double DRAG_SCROLL_EDGE = 32;
    private const double DRAG_SCROLL_STEP = 16;
    private const int DRAG_SCROLL_INTERVAL_MS = 30;
    private const double MIN_SCROLL_DELTA = 0.1;
    private readonly HashSet<DanmuForwardRuleViewModel> selectedRules = new(ReferenceEqualityComparer.Instance);
    private IPointer? gesturePointer;
    private DanmuForwardRuleViewModel? gestureRule;
    private WorkbenchSelection? gestureSelection;
    private Point gestureStart;
    private Point dragPosition;
    private bool isDragging;
    private int? dropIndex;
    private Border? dropIndicator;
    private DispatcherTimer? dragScrollTimer;

    private void InitializeRuleInteractions()
    {
        AddHandler(PointerPressedEvent, ForwardRules_OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, ForwardRules_OnPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, ForwardRules_OnPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, ForwardRules_OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void DisposeRuleInteractions()
    {
        CancelRuleGesture();
        RemoveHandler(PointerPressedEvent, ForwardRules_OnPointerPressed);
        RemoveHandler(PointerMovedEvent, ForwardRules_OnPointerMoved);
        RemoveHandler(PointerReleasedEvent, ForwardRules_OnPointerReleased);
        RemoveHandler(KeyDownEvent, ForwardRules_OnKeyDown);

        if (dragScrollTimer is not null)
        {
            dragScrollTimer.Tick -= DragScrollTimer_OnTick;
        }
    }

    private void RefreshSelectionVisuals()
    {
        foreach (var card in RuleCards())
        {
            card.Classes.Set("selected", card.DataContext is DanmuForwardRuleViewModel rule && selectedRules.Contains(rule));
        }

        foreach (var handle in ForwardRulesItems.GetVisualDescendants().OfType<Button>()
                     .Where(button => button.Classes.Contains("forwardDragHandle")))
        {
            handle.Classes.Set("selected", handle.DataContext is DanmuForwardRuleViewModel rule && selectedRules.Contains(rule));
        }

        ForwardCopyButton.IsEnabled = Context.CurrentRoom?.ForwardRules.Count > 0;
    }

    private IEnumerable<Border> RuleCards()
    {
        return ForwardRulesItems.GetVisualDescendants().OfType<Border>()
            .Where(card => card.Name == "ForwardRuleCard");
    }

    private void ToggleRuleSelection(DanmuForwardRuleViewModel rule)
    {
        if (Context.CurrentRoom?.ForwardRules.Contains(rule) != true)
        {
            return;
        }

        if (!selectedRules.Add(rule))
        {
            selectedRules.Remove(rule);
        }

        RefreshSelectionVisuals();
    }

    private void ForwardRules_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && gesturePointer is not null)
        {
            CancelRuleGesture();
            e.Handled = true;
        }
    }

    private void ForwardRules_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var handle = FindDragHandle(e.Source);

        if (handle?.DataContext is not DanmuForwardRuleViewModel rule ||
            !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed ||
            Context.CurrentRoom?.ForwardRules.Contains(rule) != true)
        {
            return;
        }

        CancelRuleGesture();
        gesturePointer = e.Pointer;
        gestureRule = rule;
        gestureSelection = Context.CaptureSelection();
        gestureStart = e.GetPosition(this);
        handle.Focus();
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void ForwardRules_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(gesturePointer, e.Pointer) || gestureRule is null)
        {
            return;
        }

        if (!Context.IsCurrent(gestureSelection))
        {
            CancelRuleGesture();
            return;
        }

        dragPosition = e.GetPosition(ForwardRulesScrollViewer);

        if (!isDragging)
        {
            var movement = e.GetPosition(this) - gestureStart;
            var tolerance = TopLevel.GetTopLevel(this)!.PlatformSettings!.GetTapSize(e.Pointer.Type);

            if (Math.Abs(movement.X) <= tolerance.Width && Math.Abs(movement.Y) <= tolerance.Height)
            {
                return;
            }

            isDragging = true;

            if (!selectedRules.Contains(gestureRule))
            {
                selectedRules.Clear();
                selectedRules.Add(gestureRule);
            }

            RefreshSelectionVisuals();
            StartDragScrolling();
        }

        UpdateDropTarget();
        e.Handled = true;
    }

    private async void ForwardRules_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(gesturePointer, e.Pointer) || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        var selection = gestureSelection;
        var rule = gestureRule;
        var wasDragging = isDragging;
        var insertionIndex = dropIndex;
        var movingRules = selectedRules.ToArray();
        CancelRuleGesture();
        e.Handled = true;

        if (!Context.IsCurrent(selection) || rule is null)
        {
            return;
        }

        if (!wasDragging)
        {
            ToggleRuleSelection(rule);
            return;
        }

        if (insertionIndex is not { } index)
        {
            return;
        }

        try
        {
            await selection!.Room.MoveForwardRulesAsync(movingRules, index);
        }
        catch (Exception exception)
        {
            if (!isPanelDisposed && Context.IsCurrent(selection))
            {
                ShowClipboardStatus($"排序保存失败：{exception.Message}", true);
            }
        }
    }

    private static Button? FindDragHandle(object? source)
    {
        if (source is not Control control)
        {
            return null;
        }

        return control.GetSelfAndVisualAncestors().OfType<Button>()
            .FirstOrDefault(button => button.Classes.Contains("forwardDragHandle"));
    }

    private void UpdateDropTarget()
    {
        ClearDropIndicator();

        if (Context.CurrentRoom is not { } room || !new Rect(ForwardRulesScrollViewer.Bounds.Size).Contains(dragPosition))
        {
            return;
        }

        var cards = RuleCards().Where(card => card.DataContext is DanmuForwardRuleViewModel)
            .ToDictionary(card => (DanmuForwardRuleViewModel)card.DataContext!);
        var index = 0;

        foreach (var rule in room.ForwardRules)
        {
            if (!cards.TryGetValue(rule, out var card) || card.TranslatePoint(default, ForwardRulesScrollViewer) is not { } origin)
            {
                return;
            }

            if (dragPosition.Y < origin.Y + card.Bounds.Height / 2)
            {
                break;
            }

            index++;
        }

        var anchorIndex = Math.Min(index, room.ForwardRules.Count - 1);

        if (anchorIndex < 0 || cards[room.ForwardRules[anchorIndex]].GetVisualParent() is not Grid parent)
        {
            return;
        }

        dropIndex = index;
        dropIndicator = parent.Children.OfType<Border>().Single(border => border.Name == "ForwardDropIndicator");
        dropIndicator.VerticalAlignment = index == room.ForwardRules.Count ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        dropIndicator.IsVisible = true;
    }

    private void ClearDropIndicator()
    {
        if (dropIndicator is not null)
        {
            dropIndicator.IsVisible = false;
            dropIndicator = null;
        }

        dropIndex = null;
    }

    private void CancelRuleGesture()
    {
        var pointer = gesturePointer;
        gesturePointer = null;
        gestureRule = null;
        gestureSelection = null;
        isDragging = false;
        dragScrollTimer?.Stop();
        ClearDropIndicator();
        pointer?.Capture(null);
    }

    private void StartDragScrolling()
    {
        if (dragScrollTimer is null)
        {
            dragScrollTimer = new()
            {
                Interval = TimeSpan.FromMilliseconds(DRAG_SCROLL_INTERVAL_MS)
            };
            dragScrollTimer.Tick += DragScrollTimer_OnTick;
        }

        dragScrollTimer.Start();
    }

    private void DragScrollTimer_OnTick(object? sender, EventArgs e)
    {
        if (!isDragging || !Context.IsCurrent(gestureSelection))
        {
            CancelRuleGesture();
            return;
        }

        var height = ForwardRulesScrollViewer.Viewport.Height;
        var direction = dragPosition.Y < DRAG_SCROLL_EDGE
            ? -Math.Clamp((DRAG_SCROLL_EDGE - dragPosition.Y) / DRAG_SCROLL_EDGE, 0, 1)
            : Math.Clamp((dragPosition.Y - height + DRAG_SCROLL_EDGE) / DRAG_SCROLL_EDGE, 0, 1);
        var maximumOffset = Math.Max(0, ForwardRulesScrollViewer.Extent.Height - height);
        var nextOffset = Math.Clamp(ForwardRulesScrollViewer.Offset.Y + direction * DRAG_SCROLL_STEP, 0, maximumOffset);

        if (Math.Abs(nextOffset - ForwardRulesScrollViewer.Offset.Y) > MIN_SCROLL_DELTA)
        {
            ForwardRulesScrollViewer.Offset = new(ForwardRulesScrollViewer.Offset.X, nextOffset);
            ForwardRulesScrollViewer.UpdateLayout();
            UpdateDropTarget();
        }
    }
}
