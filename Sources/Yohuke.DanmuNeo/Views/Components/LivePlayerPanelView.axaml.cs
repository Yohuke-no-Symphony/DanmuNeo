using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 复用播放会话的追帧面板，Dock 迁移与业务停止分别处理。
/// </summary>
public partial class LivePlayerPanelView : WorkbenchRoomPanel
{
    private NativeWebView? liveWebView;
    private EventHandler<WebViewNavigationCompletedEventArgs>? navigationCompleted;
    private IDisposable? nativeReparenting;
    private int migrationDepth;
    private long hostGeneration;
    private bool isAttached;
    private bool isDisposed;

    /// <summary>
    /// 创建并连接播放器面板。
    /// </summary>
    public LivePlayerPanelView(WorkbenchContext context) : this()
    {
        InitializeContext(context);
        context.SelectionChanged += Context_OnSelectionChanged;
    }

    /// <summary>
    /// 创建供 XAML 加载器使用的面板实例，运行时通过带上下文的构造入口连接。
    /// </summary>
    public LivePlayerPanelView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 在容器关系改变前保持原生控件，结束时按新的宿主重新挂载。
    /// </summary>
    public IDisposable BeginLayoutMigration()
    {
        if (migrationDepth++ == 0)
        {
            nativeReparenting = liveWebView?.BeginReparenting(false);
        }

        return new LivePlayerMigrationScope(() =>
        {
            if (--migrationDepth != 0)
            {
                return;
            }

            nativeReparenting?.Dispose();
            nativeReparenting = null;

            if (isAttached)
            {
                UpdateLivePlayerHost();
            }
            else
            {
                ClearLivePlayerHost();
            }
        });
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        isAttached = true;
        Localization.LanguageChanged += OnLanguageChanged;
        UpdateLivePlayerHost();
        SyncLivePlayerLanguage();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        isAttached = false;
        NotifyCaptureAvailability();
        Localization.LanguageChanged -= OnLanguageChanged;

        if (migrationDepth == 0)
        {
            ClearLivePlayerHost();
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override void OnCurrentRoomChanged(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
        if (oldRoom is not null)
        {
            oldRoom.PropertyChanged -= Room_OnPropertyChanged;
        }

        ClearLivePlayerHost();

        if (newRoom is not null)
        {
            newRoom.PropertyChanged += Room_OnPropertyChanged;
        }

        UpdateLivePlayerHost();
    }

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
        isDisposed = true;
        ReleaseCaptureBridge();
        Localization.LanguageChanged -= OnLanguageChanged;

        if (Context is not null)
        {
            Context.SelectionChanged -= Context_OnSelectionChanged;
        }

        nativeReparenting?.Dispose();
        nativeReparenting = null;
        ClearLivePlayerHost();
    }

    private void Context_OnSelectionChanged(object? sender, EventArgs e)
    {
        UpdateLivePlayerHost();
    }

    private void Room_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(LiveRoomTabViewModel.LivePlayerUrl) or
            nameof(LiveRoomTabViewModel.IsLivePlayerVisible) or
            nameof(LiveRoomTabViewModel.LivePlayerVolumePercent) or
            nameof(LiveRoomTabViewModel.IsLivePlayerMuted)))
        {
            return;
        }

        var selection = Context.CaptureSelection();
        Dispatcher.UIThread.Post(() =>
        {
            if (Context.IsCurrent(selection) && !isDisposed)
            {
                UpdateLivePlayerHost();
            }
        }, DispatcherPriority.Background);
    }

    private void UpdateLivePlayerHost()
    {
        if (isDisposed || !isAttached)
        {
            return;
        }

        if (Context.CurrentRoom is not { IsLivePlayerVisible: true } room ||
            !Uri.TryCreate(room.LivePlayerUrl, UriKind.Absolute, out var uri))
        {
            ClearLivePlayerHost();
            return;
        }

        if (liveWebView is null)
        {
            var generation = ++hostGeneration;
            var selection = Context.CaptureSelection();
            liveWebView = new();
            liveWebView.EnvironmentRequested += LiveWebView_OnEnvironmentRequested;
            navigationCompleted = (sender, _) =>
            {
                if (generation == hostGeneration && ReferenceEquals(sender, liveWebView) &&
                    Context.IsCurrent(selection))
                {
                    SyncLivePlayerAudio();
                    SyncLivePlayerLanguage();
                    SyncCaptureGesture();
                }
            };
            liveWebView.NavigationCompleted += navigationCompleted;
            LivePlayerHost.Child = liveWebView;
        }

        if (liveWebView.Source != uri)
        {
            ResetCaptureHost();
            liveWebView.Source = uri;
        }

        SyncLivePlayerAudio();
        NotifyCaptureAvailability();
    }

    private void ClearLivePlayerHost()
    {
        hostGeneration++;
        ResetCaptureHost();

        if (liveWebView is not null)
        {
            liveWebView.EnvironmentRequested -= LiveWebView_OnEnvironmentRequested;
            liveWebView.NavigationCompleted -= navigationCompleted;
            liveWebView.Source = new("about:blank");
            LivePlayerHost.Child = null;
            liveWebView = null;
            navigationCompleted = null;
        }

        NotifyCaptureAvailability();
    }

    private static void LiveWebView_OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = false;

        if (e is AppleWKWebViewEnvironmentRequestedEventArgs apple)
        {
            apple.NonPersistentDataStore = true;
        }
    }

    private void SyncLivePlayerAudio()
    {
        if (Context.CurrentRoom is not { } room || liveWebView is null)
        {
            return;
        }

        var volume = Math.Clamp(room.LivePlayerVolumePercent / 100, 0, 1)
            .ToString("0.###", CultureInfo.InvariantCulture);
        var muted = room.IsLivePlayerMuted ? "true" : "false";

        try
        {
            liveWebView.InvokeScript(
                $"window.yohukeSetLivePlayerAudio && window.yohukeSetLivePlayerAudio({volume}, {muted});");
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        var generation = hostGeneration;
        var selection = Context.CaptureSelection();
        Dispatcher.UIThread.Post(() =>
        {
            if (generation == hostGeneration && Context.IsCurrent(selection) && !isDisposed && isAttached)
            {
                SyncLivePlayerLanguage();
            }
        }, DispatcherPriority.Background);
    }

    private void SyncLivePlayerLanguage()
    {
        if (liveWebView is null || isDisposed || !isAttached)
        {
            return;
        }

        try
        {
            liveWebView.InvokeScript(LivePlayerService.CreateLocalizationScript());
        }
        catch (InvalidOperationException)
        {
        }
    }
}
