using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Menus;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class WorkbenchSession : IAsyncDisposable
{
    private readonly Window mainWindow;
    private readonly Menu windowMenu;
    private readonly MainWindowViewModel application;
    private readonly WorkbenchWindowRegistry windows;
    private readonly WorkbenchAuxiliaryCoordinator auxiliary;
    private readonly WorkbenchShortcutRouter shortcuts;
    private readonly WorkbenchMenuCatalog menuCatalog;
    private readonly WorkbenchMenuIcons menuIcons = new();
    private readonly Dictionary<Window, WorkbenchMenuProjection> menuProjections = [];
    private readonly WorkbenchApplicationMenu? applicationMenu;
    private readonly bool supportsNativeMenus;
    private bool disposed;

    private WorkbenchSession(Window mainWindow, Menu windowMenu, MainWindowViewModel application,
        WorkspaceLayoutFile layout, WorkspaceLayoutStore store, bool supportsNativeMenus)
    {
        this.mainWindow = mainWindow;
        this.windowMenu = windowMenu;
        this.application = application;
        this.supportsNativeMenus = supportsNativeMenus;
        Context = new(application);
        Panels = new(Context);
        windows = new(mainWindow);
        windows.RegisterBusinessWindow(mainWindow);
        var views = Panels.Panels.ToDictionary(pair => pair.Key, pair => pair.Value);
        views.Add(WorkbenchPanelIds.WORKSPACE, new WorkspaceSidebarView { DataContext = application });
        Layouts = new(mainWindow, views, layout, store, windows.RegisterBusinessWindow,
            windows.UnregisterBusinessWindow, Panels.BeginLayoutMigration);
        Panels.SetPanelActivator(Layouts.ShowPanel);
        Layouts.PanelClosedByUser += OnPanelClosed;
        Layouts.PersistenceFailed += OnPersistenceFailed;
        auxiliary = new(application, windows, Layouts);
        menuCatalog = new(Layouts, auxiliary.OpenSettings, auxiliary.SaveLayoutAs, auxiliary.ManageLayouts,
            auxiliary.OpenAbout, auxiliary.CheckForUpdates, () => windows.IsInteractionBlocked);
        shortcuts = new(Context, Panels, Layouts.ShowPanel, auxiliary.OpenSettings, () => windows.IsInteractionBlocked);
        windows.BusinessWindowAdded += AttachWindow;
        windows.BusinessWindowRemoved += DetachWindow;
        windows.ModalChanged += OnModalChanged;

        foreach (var window in windows.BusinessWindows)
        {
            AttachWindow(window);
        }

        if (supportsNativeMenus && Application.Current is { } current)
        {
            applicationMenu = new(current, menuCatalog, menuIcons);
        }

        mainWindow.Closed += OnMainWindowClosed;
        application.PropertyChanged += OnApplicationChanged;
        Panels.LyricLibraryRequested += OpenLyricLibrary;
        UpdateMenuMode();
        OnModalChanged();
    }

    internal WorkbenchContext Context { get; }
    internal WorkbenchPanelSet Panels { get; }
    internal WorkbenchLayoutController Layouts { get; }
    internal WorkbenchWindowRegistry Windows => windows;
    internal WorkbenchMenuCatalog MenuCatalog => menuCatalog;

    internal static async Task<WorkbenchSession> CreateAsync(Window mainWindow, Menu menu, MainWindowViewModel application,
        bool? supportsNativeMenus = null)
    {
        var store = new WorkspaceLayoutStore(application.ConfigDirectory, () => application.Settings.BackupRetentionCount);
        var layout = await store.LoadAsync(application.Settings);
        return new(mainWindow, menu, application, layout, store, supportsNativeMenus ?? OperatingSystem.IsMacOS());
    }

    internal async Task SaveForShutdownAsync()
    {
        Panels.StopHeldActions();
        application.CancelShortcutRecording();

        if (application.IsSettingsOpen)
        {
            await application.CloseSettingsAsync();
        }

        await application.SaveAsync();
        await Layouts.FlushAsync();
    }

    internal void SetShutdownPending(bool value)
    {
        windows.SetShutdownPending(value);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await Layouts.DisposeAsync();
        disposed = true;
        application.PropertyChanged -= OnApplicationChanged;
        Panels.LyricLibraryRequested -= OpenLyricLibrary;
        Layouts.PanelClosedByUser -= OnPanelClosed;
        Layouts.PersistenceFailed -= OnPersistenceFailed;
        auxiliary.Dispose();
        windows.Dispose();
        windows.BusinessWindowAdded -= AttachWindow;
        windows.BusinessWindowRemoved -= DetachWindow;
        windows.ModalChanged -= OnModalChanged;
        shortcuts.Dispose();
        menuCatalog.Dispose();
        applicationMenu?.Dispose();
        Panels.Dispose();
        Context.Dispose();
        application.Dispose();
    }

    private void AttachWindow(Window window)
    {
        shortcuts.Attach(window);
        var projection = new WorkbenchMenuProjection(menuCatalog, menuIcons, window == mainWindow ? windowMenu : null);
        menuProjections.Add(window, projection);
        projection.SetNativeMenuVisible(supportsNativeMenus && !application.UseWindowMenuOnMac);

        if (supportsNativeMenus)
        {
            NativeMenu.SetMenu(window, projection.NativeMenu);
        }
    }

    private void DetachWindow(Window window)
    {
        shortcuts.Detach(window);

        if (menuProjections.Remove(window, out var projection))
        {
            projection.Dispose();
        }
    }

    private void OnPanelClosed(string id)
    {
        if (id == WorkbenchPanelIds.LIVE_PLAYER)
        {
            Context.CurrentRoom?.StopLivePlayer();
        }
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        mainWindow.Closed -= OnMainWindowClosed;
        Dispatcher.UIThread.Post(menuIcons.Dispose, DispatcherPriority.Background);
    }

    private void OnPersistenceFailed(Exception exception)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed)
            {
                application.StatusMessage = $"布局保存失败：{exception.Message}";
            }
        });
    }

    private void OnApplicationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.UseWindowMenuOnMac))
        {
            UpdateMenuMode();
        }
    }

    private void UpdateMenuMode()
    {
        var useNative = supportsNativeMenus && !application.UseWindowMenuOnMac;
        windowMenu.IsVisible = !useNative;

        foreach (var projection in menuProjections.Values)
        {
            projection.SetNativeMenuVisible(useNative);
        }
    }

    private void OnModalChanged()
    {
        Panels.SetInteractionBlocked(windows.IsInteractionBlocked);
        menuCatalog.Refresh();
    }

    private void OpenLyricLibrary(Window? owner)
    {
        if (windows.FindAuxiliary("lyric-library") is { } existing)
        {
            existing.Activate();
            return;
        }

        application.OpenLyricLibraryPicker();
        var picker = new LyricLibraryPickerView(Context);
        picker.CloseRequested += (_, _) => windows.CloseAuxiliary("lyric-library");
        var window = windows.OpenAuxiliary("lyric-library", "歌词库", () => picker, true,
            owner: owner ?? mainWindow, width: 760, height: 640);
        window.Closed += (_, _) => picker.Dispose();
    }
}
