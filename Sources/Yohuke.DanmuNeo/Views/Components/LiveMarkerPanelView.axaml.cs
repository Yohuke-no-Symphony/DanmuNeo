using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 直播记录面板，负责文件选择、表格编辑事务与输入焦点边界。
/// </summary>
public partial class LiveMarkerPanelView : WorkbenchRoomPanel
{
    private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<Visual> observedVisuals = [];
    private readonly Func<TopLevel, FilePickerSaveOptions, Task<string?>> saveFilePicker;
    private readonly Func<TopLevel, FilePickerOpenOptions, Task<IReadOnlyList<string>>> openFilePicker;
    private LiveMarkerViewModel? observedMarker;
    private LiveMarkerEntryViewModel? editingRow;
    private Window? observedWindow;
    private bool attached;
    private bool fileActionPending;
    private bool disposed;

    /// <summary>
    /// 创建供 XAML 加载器使用的设计时面板。
    /// </summary>
    public LiveMarkerPanelView()
    {
        saveFilePicker = PickSaveFileAsync;
        openFilePicker = PickOpenFileAsync;
        InitializeComponent();
        clockTimer.Tick += OnClockTick;
    }

    /// <summary>
    /// 将记录面板连接到当前工作台房间。
    /// </summary>
    public LiveMarkerPanelView(WorkbenchContext context) : this()
    {
        InitializeContext(context);
    }

    internal LiveMarkerPanelView(WorkbenchContext context,
        Func<TopLevel, FilePickerSaveOptions, Task<string?>> saveFilePicker,
        Func<TopLevel, FilePickerOpenOptions, Task<IReadOnlyList<string>>> openFilePicker) : this(context)
    {
        this.saveFilePicker = saveFilePicker;
        this.openFilePicker = openFilePicker;
    }

    /// <summary>
    /// 由工作台注入的未保存内容确认入口；未注入时默认取消离开。
    /// </summary>
    public Func<LiveMarkerViewModel, Task<LiveMarkerLeaveDecision>>? ConfirmLeaveAsync { get; set; }

    internal bool IsClockRunning => clockTimer.IsEnabled;

    /// <summary>
    /// 提交当前单元格与行；验证失败时保留编辑状态并返回 false。
    /// </summary>
    public bool CommitEditing()
    {
        if (editingRow is null)
        {
            return true;
        }

        if (!MarkerDataGrid.IsAttachedToVisualTree() || observedWindow is not { IsVisible: true })
        {
            if (editingRow.HasErrors)
            {
                return false;
            }

            editingRow.EndEdit();
            editingRow = null;
            return true;
        }

        if (!MarkerDataGrid.CommitEdit(DataGridEditingUnit.Cell, true) || editingRow?.HasErrors == true)
        {
            return false;
        }

        return MarkerDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    /// <summary>
    /// 聚焦此面板独立的记录输入框。
    /// </summary>
    public void FocusInput()
    {
        if (observedMarker?.CanEdit == true)
        {
            MarkerInput.Focus();
            MarkerInput.CaretIndex = MarkerInput.Text?.Length ?? 0;
        }
    }

    /// <summary>
    /// 由拥有本面板焦点的工作台路由执行记录操作，保留文本框自身的编辑快捷键。
    /// </summary>
    public bool TryHandleShortcut(string actionKey, bool inTextInput)
    {
        if (disposed || observedMarker is not { CanUseFiles: true } marker ||
            WorkbenchTextInput.IsComposing(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()))
        {
            return false;
        }

        if (inTextInput && (actionKey is "liveMarker.undo" or "liveMarker.redo" or "liveMarker.delete"))
        {
            return false;
        }

        if ((actionKey is "liveMarker.add" or "liveMarker.lockTime" or "liveMarker.unlockTime") && !IsInputFocused())
        {
            return false;
        }

        if (actionKey is "liveMarker.new" or "liveMarker.open" or "liveMarker.save" or "liveMarker.saveAs" or
            "liveMarker.close" or "liveMarker.exportText" or "liveMarker.exportExcel")
        {
            _ = RunFileActionAsync(actionKey);
            return true;
        }

        if (actionKey.StartsWith("liveMarker.color", StringComparison.Ordinal) &&
            int.TryParse(actionKey["liveMarker.color".Length..], out var colorIndex) &&
            colorIndex >= 1 && colorIndex <= marker.ColorOptions.Count)
        {
            if (!marker.CanEdit)
            {
                return false;
            }

            marker.SelectedColor = marker.ColorOptions[colorIndex - 1].Value;
            return true;
        }

        var command = actionKey switch
        {
            "liveMarker.undo" => marker.UndoCommand,
            "liveMarker.redo" => marker.RedoCommand,
            "liveMarker.delete" => marker.DeleteSelectedCommand,
            "liveMarker.add" => marker.AddMarkerCommand,
            "liveMarker.lockTime" => marker.LockTimeCommand,
            "liveMarker.unlockTime" => marker.UnlockTimeCommand,
            _ => null
        };

        if (command is null)
        {
            return false;
        }

        if (!CommitEditing())
        {
            return true;
        }

        ExecuteCommand(command, marker);
        return true;
    }

    /// <inheritdoc/>
    protected override void OnCurrentRoomChanging(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
        if (!CommitEditing())
        {
            CancelEditing();
        }
    }

    /// <inheritdoc/>
    protected override void OnCurrentRoomChanged(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
        if (observedMarker is not null)
        {
            observedMarker.PropertyChanged -= OnMarkerPropertyChanged;
        }

        observedMarker = newRoom?.LiveMarker;

        if (observedMarker is not null)
        {
            observedMarker.PropertyChanged += OnMarkerPropertyChanged;
            _ = observedMarker.RestoreAsync();
        }

        UpdateRealTimeColumn();
        UpdateClockTimer();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;

        foreach (var visual in this.GetVisualAncestors().Prepend(this))
        {
            observedVisuals.Add(visual);
            visual.PropertyChanged += OnVisibilityChanged;
        }

        observedWindow = TopLevel.GetTopLevel(this) as Window;
        UpdateClockTimer();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (!CommitEditing())
        {
            CancelEditing();
        }

        StopObservingVisuals();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
        disposed = true;
        StopObservingVisuals();
        clockTimer.Tick -= OnClockTick;
        ConfirmLeaveAsync = null;
    }

    private void StopObservingVisuals()
    {
        attached = false;
        clockTimer.Stop();

        foreach (var visual in observedVisuals)
        {
            visual.PropertyChanged -= OnVisibilityChanged;
        }

        observedVisuals.Clear();
        observedWindow = null;
    }

    private void OnVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == Window.WindowStateProperty)
        {
            UpdateClockTimer();
        }
    }

    private void UpdateClockTimer()
    {
        if (!disposed && attached && observedMarker is not null && observedVisuals.All(visual => visual.IsVisible) &&
            observedWindow?.WindowState != WindowState.Minimized)
        {
            observedMarker.RefreshClock();
            clockTimer.Start();
        }
        else
        {
            clockTimer.Stop();
        }
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        observedMarker?.RefreshClock();
    }

    private void OnMarkerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveMarkerViewModel.ShowRealTimeColumn))
        {
            UpdateRealTimeColumn();
        }
    }

    private void UpdateRealTimeColumn()
    {
        MarkerDataGrid.Columns[1].IsVisible = observedMarker?.ShowRealTimeColumn ?? true;
    }

    private void DataGrid_OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        editingRow = e.Row.DataContext as LiveMarkerEntryViewModel;
        e.Cancel = editingRow is null || observedMarker?.CanEdit != true;
    }

    private void DataGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.DataContext is LiveMarkerEntryViewModel { HasErrors: true })
        {
            e.Cancel = true;
        }
    }

    private void DataGrid_OnRowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit && e.Row.DataContext is LiveMarkerEntryViewModel { HasErrors: true })
        {
            e.Cancel = true;
        }
    }

    private void DataGrid_OnRowEditEnded(object? sender, DataGridRowEditEndedEventArgs e)
    {
        editingRow = null;
    }

    private void CancelEditing()
    {
        var row = editingRow;

        if (MarkerDataGrid.IsAttachedToVisualTree() && observedWindow is { IsVisible: true })
        {
            MarkerDataGrid.CancelEdit(DataGridEditingUnit.Cell);
            MarkerDataGrid.CancelEdit(DataGridEditingUnit.Row);
        }

        row?.CancelEdit();
        editingRow = null;
    }

    private void Input_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || observedMarker is not { CanEdit: true } marker || WorkbenchTextInput.IsComposing(e.Source))
        {
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            if (marker.AddMarkerCommand.CanExecute(null) && CommitEditing())
            {
                e.Handled = true;
                ExecuteCommand(marker.AddMarkerCommand, marker);

                if (marker.SelectedEntry is { } entry)
                {
                    MarkerDataGrid.ScrollIntoView(entry, null);
                }
            }
        }
        else if (e.Key == Key.Escape && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            e.Handled = true;
            ExecuteCommand(e.KeyModifiers == KeyModifiers.Shift ? marker.UnlockTimeCommand : marker.LockTimeCommand, marker);
        }
    }

    private void DataGrid_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            var inTextInput = e.Source is TextBox || e.Source is Control control && control.FindAncestorOfType<TextBox>() is not null;
            e.Handled = TryHandleShortcut("liveMarker.delete", inTextInput);
        }
    }

    private static void ExecuteCommand(ICommand command, LiveMarkerViewModel marker)
    {
        try
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            marker.ReportError(exception);
        }
    }

    private bool IsInputFocused()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return ReferenceEquals(focused, MarkerInput) || focused is Control control &&
            control.GetVisualAncestors().Contains(MarkerInput);
    }

    internal async Task RunFileActionAsync(string actionKey)
    {
        if (fileActionPending || disposed || observedMarker is not { CanUseFiles: true } marker ||
            TopLevel.GetTopLevel(this) is not { } topLevel || !CommitEditing())
        {
            return;
        }

        var selection = Context.CaptureSelection();
        fileActionPending = true;

        try
        {
            switch (actionKey)
            {
                case "liveMarker.new":
                case "liveMarker.saveAs":
                {
                    if (actionKey == "liveMarker.saveAs" && !marker.HasDocument)
                    {
                        return;
                    }

                    var path = await saveFilePicker(topLevel, new()
                    {
                        Title = Localization.Get(actionKey == "liveMarker.new" ? "LiveMarker.New" : "LiveMarker.SaveAs"),
                        SuggestedFileName = marker.HasDocument && actionKey == "liveMarker.saveAs"
                            ? marker.FileName : SuggestedFileName(),
                        DefaultExtension = "yaml",
                        FileTypeChoices = [YamlFileType()],
                        ShowOverwritePrompt = true
                    });

                    if (path is null || !IsCurrent(selection))
                    {
                        return;
                    }

                    path = EnsureExtension(path, "yaml");

                    if (actionKey == "liveMarker.new")
                    {
                        if (await PrepareToLeaveAsync(marker, selection))
                        {
                            await marker.NewAsync(path);
                        }
                    }
                    else if (marker.HasDocument)
                    {
                        await marker.SaveAsAsync(path);
                    }

                    break;
                }
                case "liveMarker.open":
                {
                    var paths = await openFilePicker(topLevel, new()
                    {
                        Title = Localization.Get("LiveMarker.Open"),
                        AllowMultiple = false,
                        FileTypeFilter = [YamlFileType()]
                    });

                    if (paths.Count > 0 && IsCurrent(selection) && await PrepareToLeaveAsync(marker, selection))
                    {
                        await marker.OpenAsync(paths[0]);
                    }

                    break;
                }
                case "liveMarker.save":
                    await marker.SaveAsync();
                    break;
                case "liveMarker.close":
                    if (await PrepareToLeaveAsync(marker, selection))
                    {
                        await marker.CloseAsync();
                    }

                    break;
                case "liveMarker.exportText":
                case "liveMarker.exportExcel":
                {
                    if (!marker.HasDocument)
                    {
                        return;
                    }

                    var excel = actionKey == "liveMarker.exportExcel";
                    var extension = excel ? "xlsx" : "txt";
                    var path = await saveFilePicker(topLevel, new()
                    {
                        Title = Localization.Get(excel ? "LiveMarker.ExportExcel" : "LiveMarker.ExportText"),
                        SuggestedFileName = Path.ChangeExtension(marker.FileName, extension),
                        DefaultExtension = extension,
                        FileTypeChoices =
                        [
                            new(Localization.Get(excel ? "LiveMarker.WorkbookFileType" : "LiveMarker.TextFileType"))
                            {
                                Patterns = [$"*.{extension}"],
                                MimeTypes = [excel ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/plain"]
                            }
                        ],
                        ShowOverwritePrompt = true
                    });

                    if (path is null || !IsCurrent(selection) || !marker.HasDocument || !CommitEditing())
                    {
                        return;
                    }

                    path = EnsureExtension(path, extension);

                    if (excel)
                    {
                        await marker.ExportExcelAsync(path);
                    }
                    else
                    {
                        await marker.ExportTextAsync(path);
                    }

                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            marker.ReportError(exception);
        }
        finally
        {
            fileActionPending = false;
        }
    }

    private async Task<bool> PrepareToLeaveAsync(LiveMarkerViewModel marker, WorkbenchSelection? selection)
    {
        return await marker.PrepareToLeaveAsync(ConfirmLeaveAsync ?? (_ => Task.FromResult(LiveMarkerLeaveDecision.Cancel))) &&
               IsCurrent(selection);
    }

    private bool IsCurrent(WorkbenchSelection? selection)
    {
        return !disposed && Context.IsCurrent(selection);
    }

    private string SuggestedFileName()
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var header = new string((Context.CurrentRoom?.Header ?? "live-room")
            .Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return $"live-marker-{header}-{DateTime.Now:yyyyMMdd-HHmmss}.yaml";
    }

    private static string EnsureExtension(string path, string extension)
    {
        return string.IsNullOrWhiteSpace(Path.GetExtension(path)) ? $"{path}.{extension}" : path;
    }

    private static FilePickerFileType YamlFileType()
    {
        return new(Localization.Get("LiveMarker.YamlFileType"))
        {
            Patterns = ["*.yaml", "*.yml"],
            MimeTypes = ["application/yaml", "text/yaml"]
        };
    }

    private static async Task<string?> PickSaveFileAsync(TopLevel topLevel, FilePickerSaveOptions options)
    {
        using var file = await topLevel.StorageProvider.SaveFilePickerAsync(options);
        return file is null ? null : LocalPath(file);
    }

    private static async Task<IReadOnlyList<string>> PickOpenFileAsync(TopLevel topLevel, FilePickerOpenOptions options)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        try
        {
            return files.Select(LocalPath).ToArray();
        }
        finally
        {
            foreach (var file in files)
            {
                file.Dispose();
            }
        }
    }

    private static string LocalPath(IStorageFile file)
    {
        return file.TryGetLocalPath() ?? throw new IOException(Localization.Get("LiveMarker.LocalFileRequired"));
    }

    private async void FileAction_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string action })
        {
            await RunFileActionAsync(action);
        }
    }

    private void EditAction_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string action })
        {
            TryHandleShortcut(action, false);
        }
    }
}
