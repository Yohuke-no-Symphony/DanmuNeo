using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 在列表顶部跟随新弹幕，翻阅时保留条目锚点并撤销过期回顶请求。
/// </summary>
internal sealed class DanmuFeedScrollController : IDisposable
{
    private const double LATEST_OFFSET_THRESHOLD = 1;
    private readonly ScrollViewer scrollViewer;
    private readonly ItemsControl itemsControl;
    private INotifyCollectionChanged? items;
    private ScrollBar? verticalScrollBar;
    private Control? readingAnchor;
    private object? readingItem;
    private double readingAnchorY;
    private double readingOffset;
    private long session;
    private bool isScrollPending;
    private bool isFollowingLatest = true;
    private bool isUserScrollPending;
    private bool isScrollUpdateRequired;
    private bool isUpdatingScroll;
    private bool isDisposed;

    /// <summary>
    /// 为弹幕列表注册阅读锚点管理。
    /// </summary>
    public DanmuFeedScrollController(ScrollViewer scrollViewer, ItemsControl itemsControl)
    {
        this.scrollViewer = scrollViewer;
        this.itemsControl = itemsControl;
        itemsControl.ContainerPrepared += OnContainerPrepared;
        itemsControl.ContainerClearing += OnContainerClearing;
        scrollViewer.PropertyChanged += OnScrollPropertyChanged;
        scrollViewer.LayoutUpdated += OnLayoutUpdated;
        scrollViewer.TemplateApplied += OnScrollTemplateApplied;
        scrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        scrollViewer.AddHandler(InputElement.KeyDownEvent, OnKeyDown,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        scrollViewer.AddHandler(Gestures.ScrollGestureEvent, OnScrollGesture, RoutingStrategies.Bubble, true);
        AttachScrollBar(scrollViewer.GetVisualDescendants().OfType<ScrollBar>()
            .FirstOrDefault(control => control.Orientation == Orientation.Vertical));
    }

    /// <summary>
    /// 切换监听集合；传入空集合时停止自动滚动。
    /// </summary>
    public void Attach(INotifyCollectionChanged? collection)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (items is not null)
        {
            items.CollectionChanged -= OnCollectionChanged;
        }

        items = collection;
        session++;
        isScrollPending = false;
        isUserScrollPending = false;
        isScrollUpdateRequired = false;
        readingAnchor = null;
        readingItem = null;

        if (items is not null)
        {
            items.CollectionChanged += OnCollectionChanged;
            ResetToLatest();
        }
    }

    /// <summary>
    /// 撤销历史阅读定位，立即回顶并恢复后续新弹幕跟随。
    /// </summary>
    public void ScrollToLatest()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (items is not null)
        {
            ResetToLatest();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        Attach(null);
        itemsControl.ContainerPrepared -= OnContainerPrepared;
        itemsControl.ContainerClearing -= OnContainerClearing;
        scrollViewer.PropertyChanged -= OnScrollPropertyChanged;
        scrollViewer.LayoutUpdated -= OnLayoutUpdated;
        scrollViewer.TemplateApplied -= OnScrollTemplateApplied;
        AttachScrollBar(null);
        scrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
        scrollViewer.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        scrollViewer.RemoveHandler(Gestures.ScrollGestureEvent, OnScrollGesture);

        foreach (var container in itemsControl.GetRealizedContainers())
        {
            scrollViewer.UnregisterAnchorCandidate(container);
        }

        isDisposed = true;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ResetToLatest();
        }
        else
        {
            QueueScrollUpdate();
        }
    }

    private void OnScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.OffsetProperty || items is null || isUpdatingScroll)
        {
            return;
        }

        if (isUserScrollPending)
        {
            UpdateFollowingFromUserScroll();
        }
        else if (isFollowingLatest && !IsAtLatest())
        {
            isFollowingLatest = false;
            CaptureReadingAnchor();
        }
        else if (!isFollowingLatest && !isScrollPending)
        {
            QueueScrollUpdate();
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (items is not null && !isFollowingLatest && !isScrollPending && !isUpdatingScroll)
        {
            CaptureReadingAnchor();
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y != 0 && e.KeyModifiers != KeyModifiers.Shift)
        {
            if (e.Route == RoutingStrategies.Tunnel)
            {
                BeginUserScroll();
            }
            else
            {
                CompleteUserScroll();
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Up or Key.Down)
        {
            if (e.Route == RoutingStrategies.Tunnel)
            {
                BeginUserScroll();
            }
            else
            {
                CompleteUserScroll();
            }
        }
    }

    private void OnScrollTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        AttachScrollBar(e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar"));
    }

    private void AttachScrollBar(ScrollBar? scrollBar)
    {
        if (verticalScrollBar is not null)
        {
            verticalScrollBar.Scroll -= OnScrollbarScroll;
        }

        verticalScrollBar = scrollBar;

        if (verticalScrollBar is not null)
        {
            verticalScrollBar.Scroll += OnScrollbarScroll;
        }
    }

    private void OnScrollbarScroll(object? sender, ScrollEventArgs e)
    {
        if (items is not null && e.ScrollEventType != ScrollEventType.EndScroll)
        {
            BeginUserScroll();
            CompleteUserScroll();
        }
    }

    private void OnScrollGesture(object? sender, ScrollGestureEventArgs e)
    {
        if (e.Delta.Y != 0)
        {
            BeginUserScroll();
            CompleteUserScroll();
        }
    }

    private void BeginUserScroll()
    {
        if (items is null)
        {
            return;
        }

        session++;
        isScrollUpdateRequired |= isScrollPending;
        isScrollPending = false;
        isUserScrollPending = true;
    }

    private void CompleteUserScroll()
    {
        if (items is not null && isUserScrollPending)
        {
            UpdateFollowingFromUserScroll();
            isUserScrollPending = false;

            if (isScrollUpdateRequired)
            {
                isScrollUpdateRequired = false;
                QueueScrollUpdate();
            }
        }
    }

    private void UpdateFollowingFromUserScroll()
    {
        isFollowingLatest = IsAtLatest();

        if (isFollowingLatest)
        {
            readingAnchor = null;
            readingItem = null;
        }
        else
        {
            CaptureReadingAnchor();
        }
    }

    private void CaptureReadingAnchor()
    {
        readingOffset = scrollViewer.Offset.Y;
        readingAnchor = null;
        readingItem = null;
        var firstY = double.PositiveInfinity;

        foreach (var container in itemsControl.GetRealizedContainers())
        {
            if (container.TranslatePoint(default, itemsControl) is not { } position || container.Bounds.Height <= 0)
            {
                continue;
            }

            var y = position.Y - readingOffset;

            if (y + container.Bounds.Height > 0 && y < scrollViewer.Viewport.Height && y < firstY)
            {
                readingAnchor = container;
                readingItem = itemsControl.ItemFromContainer(container);
                readingAnchorY = y;
                firstY = y;
            }
        }
    }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        scrollViewer.RegisterAnchorCandidate(e.Container);
    }

    private void OnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        scrollViewer.UnregisterAnchorCandidate(e.Container);
    }

    private bool IsAtLatest()
    {
        return scrollViewer.Offset.Y <= LATEST_OFFSET_THRESHOLD;
    }

    private void ResetToLatest()
    {
        session++;
        isScrollPending = false;
        isUserScrollPending = false;
        isScrollUpdateRequired = false;
        isFollowingLatest = true;
        readingAnchor = null;
        readingItem = null;
        SetVerticalOffset(0);
        QueueScrollUpdate();
    }

    private void QueueScrollUpdate()
    {
        if (isScrollPending)
        {
            return;
        }

        isScrollPending = true;
        var requestedSession = session;
        Dispatcher.UIThread.Post(() =>
        {
            if (requestedSession != session || isDisposed)
            {
                return;
            }

            isUpdatingScroll = true;

            try
            {
                scrollViewer.UpdateLayout();

                if (requestedSession != session || isDisposed || items is null)
                {
                    return;
                }

                var offset = isFollowingLatest ? 0 : readingOffset;

                if (!isFollowingLatest && readingAnchor is not null &&
                    itemsControl.IndexFromContainer(readingAnchor) >= 0 &&
                    ReferenceEquals(readingItem, itemsControl.ItemFromContainer(readingAnchor)) &&
                    readingAnchor.TranslatePoint(default, itemsControl) is { } position)
                {
                    offset = position.Y - readingAnchorY;
                }

                scrollViewer.Offset = new(scrollViewer.Offset.X, offset);
                scrollViewer.UpdateLayout();
            }
            finally
            {
                isUpdatingScroll = false;

                if (requestedSession == session)
                {
                    isScrollPending = false;
                }
            }

            if (requestedSession == session && !isDisposed && items is not null && !isFollowingLatest)
            {
                CaptureReadingAnchor();
            }
        }, DispatcherPriority.Background);
    }

    private void SetVerticalOffset(double offset)
    {
        isUpdatingScroll = true;

        try
        {
            scrollViewer.Offset = new(scrollViewer.Offset.X, offset);
        }
        finally
        {
            isUpdatingScroll = false;
        }
    }
}
