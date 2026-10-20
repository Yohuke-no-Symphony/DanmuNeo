using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed class WorkbenchLayoutController : IAsyncDisposable
{
    private const double CAPTURE_WINDOW_WIDTH = 1040;
    private const double CAPTURE_WINDOW_HEIGHT = 680;
    private readonly Window owner;
    private readonly Action<Window>? registerFloatingWindow;
    private readonly Action<Window>? unregisterFloatingWindow;
    private readonly Func<IDisposable?>? beginViewMigration;
    private readonly Dictionary<string, WorkbenchDockPanel> panels;
    private readonly WorkbenchDockFactory factory;
    private readonly WorkbenchDockSnapshotCodec codec;
    private readonly WorkbenchLayoutConstraints constraints;
    private readonly WorkspaceLayoutSaveScheduler persistence;
    private readonly List<WorkspaceLayoutPreset> userPresets;
    private readonly List<WorkbenchFloatingHostWindow> floatingHosts = [];
    private readonly Dictionary<IDockWindow, double> restoredWindowScales = [];
    private readonly List<INotifyPropertyChanged> watchedProperties = [];
    private readonly List<INotifyPropertyChanging> watchedChangingProperties = [];
    private readonly List<INotifyCollectionChanged> watchedCollections = [];
    private IRootDock root = null!;
    private string lastFingerprint = string.Empty;
    private string? lastScheduledFileFingerprint;
    private string? focusedPanelId;
    private IDisposable? migrationScope;
    private Task? disposal;
    private int migrationRevision;
    private bool migrationActive;
    private bool applying;
    private bool capturing;
    private bool captureQueued;
    private bool disposing;
    private bool disposed;

    internal WorkbenchLayoutController(
        Window owner,
        IReadOnlyDictionary<string, Control> panelViews,
        WorkspaceLayoutFile initialLayout,
        WorkspaceLayoutStore store,
        Action<Window>? registerFloatingWindow = null,
        Action<Window>? unregisterFloatingWindow = null,
        Func<IDisposable?>? beginViewMigration = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(panelViews);
        ArgumentNullException.ThrowIfNull(store);
        Dispatcher.UIThread.VerifyAccess();

        if (!panelViews.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(WorkbenchPanelIds.All) ||
            panelViews.Values.Any(view => view is null) ||
            panelViews.Values.Distinct(ReferenceEqualityComparer.Instance).Count() != WorkbenchPanelIds.All.Count)
        {
            throw new ArgumentException(Localization.Get("Layout.UniquePanelViewsRequired"), nameof(panelViews));
        }

        var file = WorkspaceLayoutSerializer.Copy(initialLayout);
        this.owner = owner;
        this.registerFloatingWindow = registerFloatingWindow;
        this.unregisterFloatingWindow = unregisterFloatingWindow;
        this.beginViewMigration = beginViewMigration;
        panels = panelViews.ToDictionary(pair => pair.Key, pair => new WorkbenchDockPanel(pair.Key, pair.Value), StringComparer.Ordinal);
        factory = new(CreateFloatingHost, ScheduleCapture, BeginMigration, OnDockableClosing);
        codec = new(factory, panels);
        constraints = new(panels.Values);
        persistence = new(store.SaveAsync);
        persistence.SaveFailed += OnSaveFailed;
        userPresets = file.UserPresets;
        CurrentPresetId = file.CurrentPresetId;
        Host = new() { Factory = factory, InitializeFactory = false, InitializeLayout = false };
        WorkbenchDockTemplateCatalog.Install(Host, factory.ControlRecycling);
        constraints.Attach(Host);
        owner.Opened += OnOwnerOpened;
        ApplySnapshot(file.CurrentLayout, false);
        Localization.LanguageChanged += OnLanguageChanged;
    }

    internal event EventHandler? Changed;
    internal event Action<string>? PanelClosedByUser;
    internal event Action<Exception>? PersistenceFailed;
    internal event Action? MigrationStarted;
    internal event Action? MigrationCompleted;

    internal DockControl Host { get; }
    internal IRootDock RootDock => root;
    internal IReadOnlyDictionary<string, WorkbenchDockPanel> PanelAdapters => panels;
    internal string CurrentPresetId { get; private set; }
    internal bool IsModified { get; private set; }
    internal IReadOnlyList<Window> FloatingWindows => floatingHosts.Cast<Window>().ToArray();
    internal IReadOnlyList<WorkspaceLayoutPreset> Presets => WorkspaceLayoutPresets.BuiltIn
        .Concat(userPresets.Select(preset => preset with { Layout = CopySnapshot(preset.Layout) })).ToArray();

    internal bool IsVisible(string panelId)
    {
        var panel = GetPanel(panelId);
        return EnumerateRoots().Any(candidate => WorkbenchDockSnapshotCodec.Enumerate(candidate).Contains(panel));
    }

    internal void ShowPanel(string panelId)
    {
        EnsureUsable();
        var panel = GetPanel(panelId);
        BeginMigration();

        if (!IsVisible(panelId))
        {
            var originalOwner = panel.OriginalOwner as IToolDock;
            var hiddenRoot = EnumerateRoots().FirstOrDefault(candidate => candidate.HiddenDockables?.Contains(panel) == true);
            var canRestoreOwner = originalOwner is not null && EnumerateRoots()
                .Any(candidate => WorkbenchDockSnapshotCodec.Enumerate(candidate).Contains(originalOwner));

            if (canRestoreOwner && hiddenRoot is not null)
            {
                factory.RestoreDockable(panel);
            }
            else
            {
                foreach (var candidate in EnumerateRoots())
                {
                    candidate.HiddenDockables?.Remove(panel);
                }

                panel.OriginalOwner = null;
                var destination = FindReopenDock();
                factory.AddDockable(destination, panel);
                if (panelId == WorkbenchPanelIds.LIVE_CAPTURES)
                {
                    OpenDefaultCaptureWindow(destination, panel);
                }
            }
        }

        Activate(panel);
        ScheduleCapture();
    }

    internal void HidePanel(string panelId, bool userInitiated = true)
    {
        EnsureUsable();
        var panel = GetPanel(panelId);

        if (!IsVisible(panelId))
        {
            return;
        }

        if (userInitiated)
        {
            PanelClosedByUser?.Invoke(panelId);
        }
        else
        {
            BeginMigration();
        }

        factory.HideDockable(panel);
        CloseEmptyFloatingHosts();
        ScheduleCapture();
    }

    internal void FloatPanel(string panelId)
    {
        EnsureUsable();
        ShowPanel(panelId);
        var panel = GetPanel(panelId);
        if (!IsFloating(panel))
        {
            factory.FloatDockable(panel);
        }
        ScheduleCapture();
    }

    internal void ApplyPreset(string presetId)
    {
        EnsureUsable();
        var preset = FindPreset(presetId) ?? throw LocalizedExceptionText.Create("Layout.PresetMissing",
            message => new ArgumentException(message, nameof(presetId)));
        CurrentPresetId = preset.Id;
        ApplySnapshot(preset.Layout, true);
        PersistAndNotify();
    }

    internal bool SaveCurrent()
    {
        EnsureUsable();
        var index = userPresets.FindIndex(preset => preset.Id == CurrentPresetId);

        if (index < 0)
        {
            return false;
        }

        userPresets[index] = userPresets[index] with { Layout = Capture() };
        PersistAndNotify();
        return true;
    }

    internal string SaveAs(string name)
    {
        EnsureUsable();
        var id = "user-" + Guid.NewGuid().ToString("N");
        var preset = new WorkspaceLayoutPreset { Id = id, Name = name?.Trim() ?? string.Empty, Layout = Capture() };
        ValidateUserPresets([.. userPresets, preset], id);
        userPresets.Add(preset);
        CurrentPresetId = id;
        PersistAndNotify();
        return id;
    }

    internal bool Rename(string presetId, string name)
    {
        EnsureUsable();
        var index = userPresets.FindIndex(preset => preset.Id == presetId);

        if (index < 0)
        {
            return false;
        }

        var updated = userPresets.ToList();
        updated[index] = updated[index] with { Name = name?.Trim() ?? string.Empty };
        ValidateUserPresets(updated, CurrentPresetId);
        userPresets[index] = updated[index];
        PersistAndNotify();
        return true;
    }

    internal bool Delete(string presetId)
    {
        return Delete(new[] { presetId });
    }

    internal bool Delete(IReadOnlyCollection<string> presetIds)
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(presetIds);
        var ids = presetIds.ToHashSet(StringComparer.Ordinal);

        if (ids.Count == 0)
        {
            return true;
        }

        if (!ids.IsSubsetOf(userPresets.Select(preset => preset.Id)))
        {
            return false;
        }

        var snapshot = Capture();
        userPresets.RemoveAll(preset => ids.Contains(preset.Id));

        if (ids.Contains(CurrentPresetId))
        {
            CurrentPresetId = WorkspaceLayoutPresets.DEFAULT;
        }

        PersistAndNotify(snapshot);
        return true;
    }

    internal void RestoreDefault()
    {
        ApplyPreset(WorkspaceLayoutPresets.DEFAULT);
    }

    internal WorkspaceLayoutSnapshot Capture()
    {
        EnsureUsable();
        capturing = true;

        try
        {
            var visible = EnumerateRoots().SelectMany(WorkbenchDockSnapshotCodec.Enumerate)
                .OfType<WorkbenchDockPanel>().Select(panel => panel.Id).ToHashSet(StringComparer.Ordinal);
            var floating = new List<WorkspaceFloatingLayout>();

            foreach (var window in root.Windows ?? [])
            {
                if (window.Layout is not { } floatingRoot || codec.CaptureRoot(floatingRoot) is not { } layout)
                {
                    continue;
                }

                var host = window.Host as Window;

                if (host?.IsVisible == true)
                {
                    window.Save();
                }

                floating.Add(new()
                {
                    Layout = layout,
                    Bounds = new()
                    {
                        X = double.IsFinite(window.X) ? window.X : 0,
                        Y = double.IsFinite(window.Y) ? window.Y : 0,
                        Width = double.IsFinite(window.Width) && window.Width > 0 ? window.Width : 720,
                        Height = double.IsFinite(window.Height) && window.Height > 0 ? window.Height : 480,
                        Scaling = host?.IsVisible == true ? host.RenderScaling
                            : restoredWindowScales.GetValueOrDefault(window, owner.RenderScaling)
                    }
                });
            }

            var focusedPanels = EnumerateRoots().Select(candidate => candidate.FocusedDockable)
                .OfType<WorkbenchDockPanel>().Where(panel => visible.Contains(panel.Id)).ToArray();
            focusedPanelId = focusedPanels.LastOrDefault(panel => panel.Owner is IDock { IsActive: true })?.Id
                ?? focusedPanels.FirstOrDefault()?.Id
                ?? (focusedPanelId is not null && visible.Contains(focusedPanelId) ? focusedPanelId : null);
            var snapshot = new WorkspaceLayoutSnapshot
            {
                Main = codec.CaptureRoot(root),
                FloatingWindows = floating,
                HiddenPanelIds = WorkbenchPanelIds.All.Where(id => !visible.Contains(id)).ToList(),
                FocusedPanelId = focusedPanelId
            };

            if (!WorkspaceLayoutValidator.TryValidate(snapshot, out _, out var errorKey))
            {
                throw LocalizedExceptionText.Create(errorKey, message => new InvalidDataException(message));
            }

            return snapshot;
        }
        finally
        {
            capturing = false;
        }
    }

    internal WorkspaceLayoutFile CaptureFile()
    {
        return CreateFile(Capture());
    }

    internal Task FlushAsync(CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        PersistAndNotify();
        return persistence.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 保存退出前的完整布局；失败时保留工作台供重试，成功后关闭浮窗和释放观察。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispatcher.UIThread.VerifyAccess();

        if (disposal is not null)
        {
            return new(disposal);
        }

        var snapshot = CaptureFile();
        disposing = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        disposal = completion.Task;
        _ = DisposeCoreAsync(snapshot, completion);
        return new(completion.Task);
    }

    private async Task DisposeCoreAsync(WorkspaceLayoutFile snapshot, TaskCompletionSource completion)
    {
        try
        {
            SchedulePersistence(snapshot);
            await persistence.FlushAsync();
            await persistence.DisposeAsync();
        }
        catch (Exception exception)
        {
            disposing = false;
            disposal = null;
            completion.TrySetException(exception);
            return;
        }

        try
        {
            owner.Opened -= OnOwnerOpened;
            Localization.LanguageChanged -= OnLanguageChanged;
            persistence.SaveFailed -= OnSaveFailed;
            Unwatch();
            applying = true;
            constraints.Dispose();
            Host.Layout = null;
            Host.UpdateLayout();
            CloseFloatingHosts();
            factory.ControlRecycling.Clear();
            restoredWindowScales.Clear();
            CompleteMigration();
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            disposed = true;
            disposing = false;
        }
    }

    private void ApplySnapshot(WorkspaceLayoutSnapshot snapshot, bool migrate)
    {
        if (!WorkspaceLayoutValidator.TryValidate(snapshot, out _, out var errorKey))
        {
            throw LocalizedExceptionText.Create(errorKey, message => new InvalidDataException(message));
        }

        if (migrate)
        {
            BeginMigration();
        }

        applying = true;

        try
        {
            Unwatch();
            Host.Layout = null;
            Host.UpdateLayout();
            CloseFloatingHosts();
            factory.ControlRecycling.Clear();
            restoredWindowScales.Clear();

            foreach (var panel in panels.Values)
            {
                panel.Owner = null;
                panel.OriginalOwner = null;
            }

            root = codec.BuildRoot(snapshot.Main);

            foreach (var id in snapshot.HiddenPanelIds)
            {
                root.HiddenDockables!.Add(panels[id]);
            }

            var screens = GetScreenAreas();

            foreach (var floating in snapshot.FloatingWindows)
            {
                var bounds = WorkspaceLayoutBounds.Clamp(floating.Bounds, screens);
                var window = factory.CreateDockWindow();
                window.Layout = codec.BuildRoot(floating.Layout);
                window.Layout.Window = window;
                window.X = bounds.X;
                window.Y = bounds.Y;
                window.Width = bounds.Width;
                window.Height = bounds.Height;
                window.Title = string.Empty;
                restoredWindowScales.Add(window, bounds.Scaling);
                root.Windows!.Add(window);
            }

            factory.InitDockable(root, null);
            Host.Layout = root;
            focusedPanelId = snapshot.FocusedPanelId;

            if (focusedPanelId is { } focused && IsVisible(focused))
            {
                Activate(panels[focused]);
            }

            if (owner.IsVisible)
            {
                PresentFloatingHosts();
            }

            Watch();
            var current = Capture();
            lastFingerprint = Fingerprint(current);
            UpdateModification(current);
        }
        finally
        {
            applying = false;
        }
    }

    private WorkbenchFloatingHostWindow CreateFloatingHost()
    {
        var host = new WorkbenchFloatingHostWindow(owner, factory, OnFloatingClosing, ScheduleCapture);
        constraints.Attach(host.DockHost);
        floatingHosts.Add(host);
        registerFloatingWindow?.Invoke(host);
        host.Closed += OnFloatingClosed;
        return host;
    }

    private void OnFloatingClosed(object? sender, EventArgs e)
    {
        if (sender is WorkbenchFloatingHostWindow host)
        {
            constraints.Detach(host.DockHost);
            host.Closed -= OnFloatingClosed;

            if (floatingHosts.Remove(host))
            {
                unregisterFloatingWindow?.Invoke(host);
            }
        }
    }

    private void OnFloatingClosing(WorkbenchFloatingHostWindow host, bool userInitiated)
    {
        if (applying || disposing || disposed || host.Window?.Layout is not { } floatingRoot)
        {
            return;
        }

        var visible = WorkbenchDockSnapshotCodec.Enumerate(floatingRoot).OfType<WorkbenchDockPanel>().ToArray();

        foreach (var panel in visible.Where(_ => userInitiated))
        {
            PanelClosedByUser?.Invoke(panel.Id);
        }

        var hidden = visible.Concat((floatingRoot.HiddenDockables ?? []).OfType<WorkbenchDockPanel>()).Distinct().ToArray();

        foreach (var panel in hidden)
        {
            if (panel.Owner is IDock oldOwner)
            {
                oldOwner.VisibleDockables?.Remove(panel);

                if (ReferenceEquals(oldOwner.ActiveDockable, panel))
                {
                    oldOwner.ActiveDockable = oldOwner.VisibleDockables?.FirstOrDefault();
                }
            }

            floatingRoot.HiddenDockables?.Remove(panel);
            root.HiddenDockables ??= factory.CreateList<IDockable>();

            if (!root.HiddenDockables.Contains(panel))
            {
                root.HiddenDockables.Add(panel);
            }

            panel.OriginalOwner = null;
            panel.Owner = root;
        }

        root.Windows?.Remove(host.Window);
        restoredWindowScales.Remove(host.Window);
        ScheduleCapture();
    }

    private void CloseEmptyFloatingHosts()
    {
        foreach (var host in floatingHosts.ToArray())
        {
            if (host.Window?.Layout is not { } floatingRoot ||
                WorkbenchDockSnapshotCodec.Enumerate(floatingRoot).OfType<WorkbenchDockPanel>().Any())
            {
                continue;
            }

            OnFloatingClosing(host, false);
            host.IsInternalClose = true;
            host.Close();
        }
    }

    private void CloseFloatingHosts()
    {
        foreach (var host in floatingHosts.ToArray())
        {
            host.IsInternalClose = true;
            constraints.Detach(host.DockHost);
            host.IsTracked = false;
            host.DockHost.Layout = null;
            host.DockHost.UpdateLayout();

            if (host.Window is { } model)
            {
                model.Layout = null;
                model.Owner = null;
            }

            host.Close();

            if (floatingHosts.Remove(host))
            {
                host.Closed -= OnFloatingClosed;
                unregisterFloatingWindow?.Invoke(host);
            }
        }
    }

    private void PresentFloatingHosts()
    {
        foreach (var window in (root.Windows ?? []).ToArray())
        {
            window.Present(false);
        }

        RefreshFloatingTitles();
    }

    private void OnOwnerOpened(object? sender, EventArgs e)
    {
        if (!disposing && !disposed)
        {
            PresentFloatingHosts();
        }
    }

    internal void RefreshFloatingTitles()
    {
        foreach (var host in floatingHosts)
        {
            if (host.Window?.Layout is not { } floatingRoot)
            {
                continue;
            }

            var title = (floatingRoot.FocusedDockable as WorkbenchDockPanel)?.Title
                ?? WorkbenchDockSnapshotCodec.Enumerate(floatingRoot).OfType<WorkbenchDockPanel>().FirstOrDefault()?.Title
                ?? Localization.Get("Workbench.Title");
            host.Title = Localization.Format("Workbench.FloatingTitle", title);
            host.Window.Title = host.Title;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var panel in panels.Values)
        {
            panel.Title = WorkbenchDockPanel.GetLocalizedTitle(panel.Id);
        }

        RefreshFloatingTitles();
    }

    private void Activate(WorkbenchDockPanel panel)
    {
        factory.SetActiveDockable(panel);

        if (panel.Owner is IDock dock)
        {
            factory.SetFocusedDockable(dock, panel);
        }

        focusedPanelId = panel.Id;
        var floating = floatingHosts.FirstOrDefault(host => host.Window?.Layout is { } floatingRoot &&
            WorkbenchDockSnapshotCodec.Enumerate(floatingRoot).Contains(panel));

        if (!applying)
        {
            if (floating?.IsVisible == true)
            {
                floating.Window?.SetActive();
            }
            else if (floating is null && owner.IsVisible)
            {
                if (owner.WindowState == WindowState.Minimized)
                {
                    owner.WindowState = WindowState.Normal;
                }

                owner.Activate();
            }
        }
    }

    private bool IsFloating(WorkbenchDockPanel panel)
    {
        return (root.Windows ?? []).Any(window => window.Layout is { } layout &&
            WorkbenchDockSnapshotCodec.Enumerate(layout).Contains(panel));
    }

    private void OpenDefaultCaptureWindow(IDock source, WorkbenchDockPanel panel)
    {
        var scaling = owner.DesktopScaling;
        var size = owner.FrameSize ?? owner.ClientSize;
        var bounds = WorkspaceLayoutBounds.Clamp(new()
        {
            X = owner.Position.X + (size.Width - CAPTURE_WINDOW_WIDTH) * scaling / 2,
            Y = owner.Position.Y + (size.Height - CAPTURE_WINDOW_HEIGHT) * scaling / 2,
            Width = CAPTURE_WINDOW_WIDTH,
            Height = CAPTURE_WINDOW_HEIGHT,
            Scaling = scaling
        }, GetScreenAreas());
        factory.SplitToWindow(source, panel, bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private WorkspaceScreenArea[] GetScreenAreas()
    {
        return owner.Screens.All.Select(screen => new WorkspaceScreenArea(screen.WorkingArea.X,
            screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height, screen.Scaling)).ToArray();
    }

    private IToolDock FindReopenDock()
    {
        var dock = WorkbenchDockSnapshotCodec.Enumerate(root).OfType<IToolDock>().FirstOrDefault();

        if (dock is not null)
        {
            return dock;
        }

        dock = factory.CreateToolDock();
        dock.Id = "workspace-reopened-" + Guid.NewGuid().ToString("N");
        dock.VisibleDockables = factory.CreateList<IDockable>();
        factory.AddDockable(root, dock);
        root.ActiveDockable = dock;
        return dock;
    }

    private IEnumerable<IRootDock> EnumerateRoots()
    {
        yield return root;

        foreach (var window in root.Windows ?? [])
        {
            if (window.Layout is { } floatingRoot)
            {
                yield return floatingRoot;
            }
        }
    }

    private void OnDockableClosing(IDockable? dockable)
    {
        if (applying || disposing || disposed || dockable is null)
        {
            return;
        }

        foreach (var panel in WorkbenchDockSnapshotCodec.Enumerate(dockable).OfType<WorkbenchDockPanel>())
        {
            if (IsVisible(panel.Id))
            {
                PanelClosedByUser?.Invoke(panel.Id);
            }
        }
    }

    private void BeginMigration()
    {
        if (applying || disposing || disposed)
        {
            return;
        }

        if (!migrationActive)
        {
            migrationActive = true;
            migrationScope = beginViewMigration?.Invoke();
            MigrationStarted?.Invoke();
        }

        var revision = ++migrationRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (revision == migrationRevision && !disposed)
            {
                CompleteMigration();
            }
        }, DispatcherPriority.Loaded);
    }

    private void CompleteMigration()
    {
        if (!migrationActive)
        {
            return;
        }

        migrationRevision++;
        migrationActive = false;
        var scope = migrationScope;
        migrationScope = null;

        try
        {
            Host.UpdateLayout();

            foreach (var host in floatingHosts)
            {
                host.DockHost.UpdateLayout();
            }

            scope?.Dispose();

            if (!disposing && !disposed)
            {
                WorkbenchTextInput.RequestNativeContextRecovery(Host);
                foreach (var host in floatingHosts)
                {
                    WorkbenchTextInput.RequestNativeContextRecovery(host.DockHost);
                }
            }
        }
        finally
        {
            MigrationCompleted?.Invoke();
        }
    }

    private void Watch()
    {
        Unwatch();
        var nodes = EnumerateRoots().SelectMany(WorkbenchDockSnapshotCodec.Enumerate).ToArray();

        foreach (var value in nodes.Cast<object>().Concat(root.Windows ?? []).Distinct(ReferenceEqualityComparer.Instance))
        {
            if (value is INotifyPropertyChanged changed)
            {
                changed.PropertyChanged += OnModelChanged;
                watchedProperties.Add(changed);
            }

            if (value is INotifyPropertyChanging changing)
            {
                changing.PropertyChanging += OnModelChanging;
                watchedChangingProperties.Add(changing);
            }

            if (value is IDock dock)
            {
                WatchCollection(dock.VisibleDockables);
            }

            if (value is IRootDock rootDock)
            {
                WatchCollection(rootDock.Windows);
                WatchCollection(rootDock.HiddenDockables);
            }
        }
    }

    private void WatchCollection(object? collection)
    {
        if (collection is INotifyCollectionChanged changed && !watchedCollections.Contains(changed))
        {
            changed.CollectionChanged += OnCollectionChanged;
            watchedCollections.Add(changed);
        }
    }

    private void Unwatch()
    {
        foreach (var changed in watchedProperties)
        {
            changed.PropertyChanged -= OnModelChanged;
        }

        foreach (var changing in watchedChangingProperties)
        {
            changing.PropertyChanging -= OnModelChanging;
        }

        foreach (var changed in watchedCollections)
        {
            changed.CollectionChanged -= OnCollectionChanged;
        }

        watchedProperties.Clear();
        watchedChangingProperties.Clear();
        watchedCollections.Clear();
    }

    private void OnModelChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (e.PropertyName == nameof(IDock.ActiveDockable))
        {
            BeginMigration();
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IDockable.Proportion) or nameof(IDock.ActiveDockable) or
            nameof(IDock.VisibleDockables) or nameof(IRootDock.FocusedDockable) or nameof(IDockWindow.Layout) or
            nameof(IDockWindow.X) or nameof(IDockWindow.Y) or nameof(IDockWindow.Width) or nameof(IDockWindow.Height))
        {
            ScheduleCapture();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleCapture();
    }

    private void ScheduleCapture()
    {
        if (applying || capturing || disposing || disposed || captureQueued)
        {
            return;
        }

        captureQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            captureQueued = false;

            if (applying || disposing || disposed)
            {
                return;
            }

            try
            {
                CloseEmptyFloatingHosts();
                Watch();
                RefreshFloatingTitles();
                var snapshot = Capture();

                if (Fingerprint(snapshot) != lastFingerprint)
                {
                    PersistAndNotify(snapshot);
                }
            }
            catch (InvalidDataException exception)
            {
                PersistenceFailed?.Invoke(exception);
            }
        }, DispatcherPriority.Background);
    }

    private void PersistAndNotify(WorkspaceLayoutSnapshot? snapshot = null)
    {
        snapshot ??= Capture();
        lastFingerprint = Fingerprint(snapshot);
        UpdateModification(snapshot);
        SchedulePersistence(CreateFile(snapshot));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SchedulePersistence(WorkspaceLayoutFile file)
    {
        var fingerprint = WorkspaceLayoutSerializer.Serialize(file);

        if (fingerprint == lastScheduledFileFingerprint)
        {
            return;
        }

        persistence.Schedule(file);
        lastScheduledFileFingerprint = fingerprint;
    }

    private WorkspaceLayoutFile CreateFile(WorkspaceLayoutSnapshot snapshot)
    {
        return WorkspaceLayoutSerializer.Copy(new()
        {
            CurrentPresetId = CurrentPresetId,
            CurrentLayout = snapshot,
            UserPresets = userPresets.ToList()
        });
    }

    private void ValidateUserPresets(List<WorkspaceLayoutPreset> presets, string currentId)
    {
        if (!WorkspaceLayoutValidator.TryValidate(new WorkspaceLayoutFile
            {
                CurrentPresetId = currentId,
                CurrentLayout = Capture(),
                UserPresets = presets
            }, out _, out var errorKey))
        {
            throw LayoutValidationFailure.CreateException(errorKey, "name");
        }
    }

    private void UpdateModification(WorkspaceLayoutSnapshot snapshot)
    {
        var preset = FindPreset(CurrentPresetId);
        IsModified = preset is null || Fingerprint(snapshot) != Fingerprint(preset.Layout);
    }

    private WorkspaceLayoutPreset? FindPreset(string id)
    {
        return WorkspaceLayoutPresets.BuiltIn.FirstOrDefault(preset => preset.Id == id)
            ?? userPresets.FirstOrDefault(preset => preset.Id == id);
    }

    private WorkbenchDockPanel GetPanel(string id)
    {
        return panels.TryGetValue(id, out var panel) ? panel : throw new ArgumentException(Localization.Get("Layout.UnknownPanelId"), nameof(id));
    }

    private void OnSaveFailed(Exception exception)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposing && !disposed)
            {
                PersistenceFailed?.Invoke(exception);
            }
        });
    }

    private void EnsureUsable()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposing || disposed, this);
    }

    private static WorkspaceLayoutSnapshot CopySnapshot(WorkspaceLayoutSnapshot snapshot)
    {
        return WorkspaceLayoutSerializer.Copy(new() { CurrentLayout = snapshot }).CurrentLayout;
    }

    private static string Fingerprint(WorkspaceLayoutSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot);
    }
}
