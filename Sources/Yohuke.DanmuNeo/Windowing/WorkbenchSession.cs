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
    private readonly Dictionary<Window, WorkbenchMenuProjection> menuProjections = [];
    private readonly WorkbenchApplicationMenu? applicationMenu;
    private bool disposed;

    private WorkbenchSession(Window mainWindow, Menu windowMenu, MainWindowViewModel application,
        WorkspaceLayoutFile layout, WorkspaceLayoutStore store)
    {
        this.mainWindow = mainWindow;
        this.windowMenu = windowMenu;
        this.application = application;
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

        if (OperatingSystem.IsMacOS() && Application.Current is { } current)
        {
            applicationMenu = new(current, menuCatalog);
        }

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

    internal static async Task<WorkbenchSession> CreateAsync(Window mainWindow, Menu menu, MainWindowViewModel application)
    {
        var store = new WorkspaceLayoutStore(application.ConfigDirectory, () => application.Settings.BackupRetentionCount);
        var layout = await store.LoadAsync(application.Settings);
        return new(mainWindow, menu, application, layout, store);
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
        var projection = new WorkbenchMenuProjection(menuCatalog, window == mainWindow ? windowMenu : null);
        menuProjections.Add(window, projection);
        NativeMenu.SetMenu(window, OperatingSystem.IsMacOS() && !application.UseWindowMenuOnMac
            ? projection.NativeMenu
            : null);
    }

    private void DetachWindow(Window window)
    {
        shortcuts.Detach(window);

        if (menuProjections.Remove(window, out var projection))
        {
            NativeMenu.SetMenu(window, null);
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
        var useNative = OperatingSystem.IsMacOS() && !application.UseWindowMenuOnMac;
        windowMenu.IsVisible = !useNative;

        foreach (var (window, projection) in menuProjections)
        {
            NativeMenu.SetMenu(window, useNative ? projection.NativeMenu : null);
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
