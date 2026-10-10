using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;

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
    private long session;
    private bool isScrollPending;
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

        if (items is not null)
        {
            items.CollectionChanged += OnCollectionChanged;
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
        else if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex == 0 && IsAtLatest())
        {
            QueueScrollToLatest();
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
        scrollViewer.Offset = new(scrollViewer.Offset.X, 0);
        QueueScrollToLatest();
    }

    private void QueueScrollToLatest()
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

            isScrollPending = false;

            if (items is not null && IsAtLatest())
            {
                scrollViewer.Offset = new(scrollViewer.Offset.X, 0);
            }
        }, DispatcherPriority.Background);
    }
}
