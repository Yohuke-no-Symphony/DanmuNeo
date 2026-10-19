using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Captures;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.ViewModels;

/// <summary>无需当前直播间即可使用的永久截图图库、筛选、备注和导出状态。</summary>
public sealed partial class LiveCapturesViewModel : ObservableObject, IDisposable
{
    private readonly ILiveCaptureStore store;
    private readonly ILiveCaptureExportService exporter;
    private readonly SemaphoreSlim operation = new(1, 1);
    private bool initialized;
    private bool refreshingFilters;
    private bool disposed;
    private bool captureSourceAvailable;
    private readonly TimeSpan noteSaveDelay;
    private CancellationTokenSource? noteSaveCancellation;
    private CancellationTokenSource? exportCancellation;
    private Task pendingNoteSave = Task.CompletedTask;
    private string? statusKey;
    private object[] statusArguments = [];

    [ObservableProperty]
    private LiveCaptureFilterOption? selectedRoom;
    [ObservableProperty]
    private LiveCaptureFilterOption? selectedSession;
    [ObservableProperty]
    private LiveCaptureItemViewModel? selectedCapture;
    [ObservableProperty]
    private bool isCapturing;
    [ObservableProperty]
    private bool isBusy;
    [ObservableProperty]
    private string statusText = "";

    /// <summary>注入独立图库和导出服务。</summary>
    public LiveCapturesViewModel(ILiveCaptureStore store, ILiveCaptureExportService exporter, TimeSpan? noteSaveDelay = null)
    {
        this.store = store;
        this.exporter = exporter;
        this.noteSaveDelay = noteSaveDelay ?? TimeSpan.FromMilliseconds(450);
        ArgumentOutOfRangeException.ThrowIfLessThan(this.noteSaveDelay, TimeSpan.Zero);
        SelectedCaptures.CollectionChanged += OnSelectedCapturesChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        RefreshFilters();
    }

    public ObservableCollection<LiveCaptureItemViewModel> Captures { get; } = [];
    public ObservableCollection<LiveCaptureItemViewModel> FilteredCaptures { get; } = [];
    public ObservableCollection<LiveCaptureItemViewModel> SelectedCaptures { get; } = [];
    public ObservableCollection<LiveCaptureFilterOption> RoomOptions { get; } = [];
    public ObservableCollection<LiveCaptureFilterOption> SessionOptions { get; } = [];
    public bool HasPendingChanges => store.HasPendingChanges;
    public bool HasPendingOriginals => store.HasPendingOriginals;
    private bool CanOperate => !disposed && !IsBusy && operation.CurrentCount > 0;
    public bool CanCapture => captureSourceAvailable && !IsCapturing && CanOperate && !store.HasPendingOriginals;
    public bool CanSave => CanOperate;
    public bool CanExportSingle => CanOperate && !IsCapturing && HasSelectedCapture;
    public bool CanExportSelected => CanOperate && !IsCapturing && HasSelectedCaptures;
    public bool CanExportFiltered => CanOperate && !IsCapturing && HasFilteredCaptures;
    public bool HasSelectedCapture => SelectedCapture is not null;
    public bool HasSelectedCaptures => SelectedCaptures.Count > 0;
    public bool HasFilteredCaptures => FilteredCaptures.Count > 0;
    internal Task PendingNoteSave => pendingNoteSave;
    public string CountText => Localization.Format("Captures.Count", FilteredCaptures.Count, Captures.Count);

    /// <summary>加载永久图库；重复调用不重复添加记录。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operation.WaitAsync(cancellationToken);
        try
        {
            IsBusy = true;
            if (initialized)
            {
                return;
            }
            var records = await store.LoadAsync(cancellationToken);
            if (disposed)
            {
                return;
            }
            foreach (var record in records)
            {
                if (Captures.All(item => item.Id != record.Id))
                {
                    Captures.Add(CreateItem(record));
                }
            }
            initialized = true;
            RefreshFilters();
            if (store.LoadWarnings.Count > 0)
            {
                SetStatus("Captures.LoadWarnings", store.LoadWarnings.Count);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SetStatus("Captures.LoadFailed", exception.Message);
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>保留原帧后尝试落盘；失败仍在图库中保留记录供保存重试。</summary>
    public async Task CaptureAsync(LiveCapturedFrame frame, string roomName,
        CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        await operation.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            IsBusy = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (store.HasPendingOriginals)
            {
                throw new InvalidOperationException(Localization.Get("Captures.SaveBeforeCapture"));
            }
            CancelNoteSaveDelay();
            var record = store.Add(frame, roomName);
            var item = CreateItem(record);
            Captures.Insert(0, item);
            RefreshFilters();
            SelectedCapture = FilteredCaptures.Contains(item) ? item : SelectedCapture;
            OnPropertyChanged(nameof(HasPendingChanges));
            NotifyOperationAvailability();
            try
            {
                await FlushCoreAsync(cancellationToken);
                SetStatus("Captures.Saved");
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                SetStatus("Captures.SaveFailed", exception.Message);
            }
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>等待已有操作及全部原图与最新备注落盘，退出流程可等待并在失败时重试。</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        CancelNoteSaveDelay();
        await operation.WaitAsync(cancellationToken);
        try
        {
            if (!disposed)
            {
                IsBusy = true;
            }
            await FlushCoreAsync(cancellationToken);
            SetStatus("Captures.Saved");
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task FlushCoreAsync(CancellationToken cancellationToken)
    {
        await store.FlushAsync(cancellationToken);
        if (disposed)
        {
            return;
        }
        foreach (var item in Captures)
        {
            item.RefreshPreview(store.GetOriginalPath(item.Id));
        }
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(CanCapture));
    }

    /// <summary>重试尚未保存的截图和备注，失败保留待写内容。</summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!TryBeginOperation())
        {
            return;
        }
        CancelNoteSaveDelay();
        try
        {
            await FlushCoreAsync(CancellationToken.None);
            SetStatus("Captures.Saved");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SetStatus("Captures.SaveFailed", exception.Message);
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>导出明确指定的记录；取消和失败不移除任何图库条目。</summary>
    public async Task ExportAsync(IEnumerable<LiveCaptureItemViewModel> items, string directory,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsCapturing || !TryBeginOperation())
        {
            return;
        }
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        exportCancellation = request;
        try
        {
            var outputs = await exporter.ExportAsync(items.Select(item => item.Snapshot).ToArray(), directory,
                request.Token);
            SetStatus("Captures.Exported", outputs.Count);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Captures.ExportCanceled");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            SetStatus("Captures.ExportFailed", exception.Message);
        }
        finally
        {
            exportCancellation = null;
            EndOperation();
        }
    }

    /// <summary>导出单张到 View 选择的位置，不改变永久记录。</summary>
    public async Task ExportToFileAsync(LiveCaptureItemViewModel item, string filePath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsCapturing || !TryBeginOperation())
        {
            return;
        }
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        exportCancellation = request;
        try
        {
            await exporter.ExportToFileAsync(item.Snapshot, filePath, request.Token);
            SetStatus("Captures.Exported", 1);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Captures.ExportCanceled");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            SetStatus("Captures.ExportFailed", exception.Message);
        }
        finally
        {
            exportCancellation = null;
            EndOperation();
        }
    }

    /// <summary>退出时取消正在合成的导出，保留全部永久原图、备注及已完成的导出文件。</summary>
    public void CancelExport()
    {
        exportCancellation?.Cancel();
    }

    /// <summary>清除房间和场次筛选，显示所有永久记录。</summary>
    [RelayCommand]
    public void ClearFilters()
    {
        refreshingFilters = true;
        SelectedRoom = RoomOptions.FirstOrDefault();
        SelectedSession = SessionOptions.FirstOrDefault();
        refreshingFilters = false;
        RefreshFilters();
    }

    /// <summary>由工作台统一同步可抓帧的当前来源，未选房时图库仍保留浏览与导出能力。</summary>
    public void SetCaptureSourceAvailable(bool available)
    {
        if (captureSourceAvailable == available)
        {
            return;
        }
        captureSourceAvailable = available;
        OnPropertyChanged(nameof(CanCapture));
    }

    /// <summary>显示抓帧失败等外部动作的可见错误，保留现有图库。</summary>
    public void ReportCaptureFailure(string message)
    {
        SetStatus("Captures.CaptureFailed", message);
    }

    /// <summary>显示文件选择器等平台导出边界的错误。</summary>
    public void ReportExportFailure(string message)
    {
        SetStatus("Captures.ExportFailed", message);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        CancelExport();
        CancelNoteSaveDelay();
        SelectedCaptures.CollectionChanged -= OnSelectedCapturesChanged;
        Localization.LanguageChanged -= OnLanguageChanged;
    }

    partial void OnSelectedCaptureChanged(LiveCaptureItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedCapture));
        OnPropertyChanged(nameof(CanExportSingle));
    }

    partial void OnIsCapturingChanged(bool value)
    {
        NotifyOperationAvailability();
    }

    partial void OnIsBusyChanged(bool value)
    {
        NotifyOperationAvailability();
    }

    partial void OnSelectedRoomChanged(LiveCaptureFilterOption? value)
    {
        if (!refreshingFilters)
        {
            SelectedSession = null;
            RefreshFilters();
        }
    }

    partial void OnSelectedSessionChanged(LiveCaptureFilterOption? value)
    {
        if (!refreshingFilters)
        {
            RefreshVisibleCaptures();
        }
    }

    private LiveCaptureItemViewModel CreateItem(LiveCaptureRecord record)
    {
        var item = new LiveCaptureItemViewModel(record, (id, note) =>
        {
            store.UpdateNote(id, note);
            OnPropertyChanged(nameof(HasPendingChanges));
            SetStatus("Captures.UnsavedNotes");
            ScheduleNoteSave();
        });
        item.RefreshPreview(store.GetOriginalPath(item.Id));
        return item;
    }

    private void RefreshFilters()
    {
        refreshingFilters = true;
        try
        {
            var roomKey = SelectedRoom?.Key ?? "";
            var rooms = new List<LiveCaptureFilterOption> { new("", Localization.Get("Captures.AllRooms")) };
            rooms.AddRange(Captures.GroupBy(item => item.RoomId).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new LiveCaptureFilterOption(group.Key, group.First().Title)));
            ReconcileOptions(RoomOptions, rooms);
            SelectedRoom = RoomOptions.FirstOrDefault(item => item.Key == roomKey) ?? RoomOptions[0];
            var sessionKey = SelectedSession?.Key ?? "";
            var sessions = new List<LiveCaptureFilterOption> { new("", Localization.Get("Captures.AllSessions")) };
            sessions.AddRange(Captures.Where(item => SelectedRoom.Key == "" || item.RoomId == SelectedRoom.Key)
                .GroupBy(item => item.BroadcastSessionId).OrderByDescending(group => group.Max(item => item.CapturedAt))
                .Select(group => new LiveCaptureFilterOption(group.Key,
                    $"{group.First().Title} · {LiveCaptureTimeFormatter.FormatTime(group.First().StartedAt)}")));
            ReconcileOptions(SessionOptions, sessions);
            SelectedSession = SessionOptions.FirstOrDefault(item => item.Key == sessionKey) ?? SessionOptions[0];
        }
        finally
        {
            refreshingFilters = false;
        }
        RefreshVisibleCaptures();
    }

    private void RefreshVisibleCaptures()
    {
        var visible = Captures.Where(item => (string.IsNullOrEmpty(SelectedRoom?.Key) || item.RoomId == SelectedRoom.Key) &&
            (string.IsNullOrEmpty(SelectedSession?.Key) || item.BroadcastSessionId == SelectedSession.Key))
            .OrderByDescending(item => item.CapturedAt).ToArray();
        if (!FilteredCaptures.SequenceEqual(visible))
        {
            FilteredCaptures.Clear();
            foreach (var item in visible)
            {
                FilteredCaptures.Add(item);
            }
        }
        foreach (var item in SelectedCaptures.Where(item => !FilteredCaptures.Contains(item)).ToArray())
        {
            SelectedCaptures.Remove(item);
        }
        if (SelectedCapture is not null && !FilteredCaptures.Contains(SelectedCapture))
        {
            SelectedCapture = null;
        }
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasFilteredCaptures));
        OnPropertyChanged(nameof(CanExportFiltered));
    }

    private static void ReconcileOptions(ObservableCollection<LiveCaptureFilterOption> target,
        IReadOnlyList<LiveCaptureFilterOption> desired)
    {
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < target.Count && target[index] == desired[index])
            {
                continue;
            }
            var existing = target.FirstOrDefault(item => item == desired[index]);
            if (existing is not null)
            {
                target.Move(target.IndexOf(existing), index);
            }
            else
            {
                target.Insert(index, desired[index]);
            }
        }
        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private void OnSelectedCapturesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSelectedCaptures));
        OnPropertyChanged(nameof(CanExportSelected));
    }

    private bool TryBeginOperation()
    {
        if (!operation.Wait(0))
        {
            return false;
        }
        IsBusy = true;
        return true;
    }

    private void EndOperation()
    {
        if (!disposed)
        {
            IsBusy = false;
        }
        operation.Release();
        if (!disposed)
        {
            NotifyOperationAvailability();
        }
    }

    private void NotifyOperationAvailability()
    {
        OnPropertyChanged(nameof(CanCapture));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanExportSingle));
        OnPropertyChanged(nameof(CanExportSelected));
        OnPropertyChanged(nameof(CanExportFiltered));
    }

    private void ScheduleNoteSave()
    {
        CancelNoteSaveDelay();
        if (disposed)
        {
            return;
        }
        noteSaveCancellation = new();
        pendingNoteSave = SaveNotesAfterDelayAsync(noteSaveCancellation.Token);
    }

    private async Task SaveNotesAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(noteSaveDelay, cancellationToken);
            await FlushCoreAsync(cancellationToken);
            if (!disposed)
            {
                SetStatus("Captures.Saved");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (!disposed)
            {
                SetStatus("Captures.SaveFailed", exception.Message);
            }
        }
    }

    private void CancelNoteSaveDelay()
    {
        var previous = noteSaveCancellation;
        noteSaveCancellation = null;
        previous?.Cancel();
        previous?.Dispose();
    }

    private void SetStatus(string key, params object[] arguments)
    {
        if (disposed)
        {
            return;
        }
        statusKey = key;
        statusArguments = arguments;
        StatusText = Localization.Format(key, arguments);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in Captures)
        {
            item.RefreshLanguage();
        }
        RefreshFilters();
        if (statusKey is not null)
        {
            StatusText = Localization.Format(statusKey, statusArguments);
        }
    }
}
