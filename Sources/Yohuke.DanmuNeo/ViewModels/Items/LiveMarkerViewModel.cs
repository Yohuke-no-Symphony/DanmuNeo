using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>一个房间标签持有的直播记录文档和编辑会话。</summary>
public sealed partial class LiveMarkerViewModel : ObservableObject, IDisposable
{
    private readonly LiveRoomTabState state;
    private readonly Stack<LiveMarkerEdit> undo = [];
    private readonly Stack<LiveMarkerEdit> redo = [];
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private LiveMarkerServices? services;
    private LiveMarkerClock? clock;
    private LiveMarkerDocument? document;
    private IDisposable? fileLease;
    private Func<string>? roomId;
    private Func<string?>? cookie;
    private Func<AppSettings>? settings;
    private Func<Task>? saveState;
    private IAppLogService? log;
    private CancellationTokenSource? debounce;
    private CancellationTokenSource? operationCancellation;
    private Task pendingAutoSave = Task.CompletedTask;
    private long generation;
    private long node;
    private long nextNode;
    private long savedNode;
    private bool restoreAttempted;
    private bool applyingPrefix;
    private bool keepEmptyDraftTime;
    private bool disposed;
    private TimeSpan lockedLiveTime;
    private DateTimeOffset lockedRealTime;
    private string draftText = "";

    /// <summary>为房间标签创建独立的编辑会话。</summary>
    public LiveMarkerViewModel(LiveRoomTabState state)
    {
        this.state = state;
        ColorOptions = Enum.GetValues<LiveMarkerColor>().Select(value => new LiveMarkerColorOption(value)).ToArray();
        Localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>已提交记录，保留输入顺序。</summary>
    public ObservableCollection<LiveMarkerEntryViewModel> Entries { get; } = [];

    /// <summary>七种记录颜色。</summary>
    public IReadOnlyList<LiveMarkerColorOption> ColorOptions { get; }

    /// <summary>当前文件路径。</summary>
    public string FilePath => document is null ? "" : state.LiveMarkerFilePath;

    /// <summary>当前文件名或未打开提示。</summary>
    public string FileName => HasDocument ? Path.GetFileName(FilePath) : Localization.Get("LiveMarker.NoFile");

    /// <summary>不可编辑的文件计时起点。</summary>
    public string StartTimeText => document?.StartTime.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) ?? "";

    /// <summary>当前是否有已打开文档。</summary>
    public bool HasDocument => document is not null;

    /// <summary>当前文档是否有尚未保存的内容。</summary>
    public bool IsDirty => HasDocument && node != savedNode;

    /// <summary>是否有尚未提交的打点草稿。</summary>
    public bool HasDraft => !string.IsNullOrWhiteSpace(DraftText);

    /// <summary>文档操作是否已准备就绪。</summary>
    public bool CanUseFiles => services is not null && !IsBusy && !disposed;

    /// <summary>是否可以编辑已打开文档。</summary>
    public bool CanEdit => CanUseFiles && HasDocument;

    /// <summary>是否可以提交打点。</summary>
    public bool CanAddMarker => CanEdit && HasDraft &&
        (!LiveMarkerTimeInput.TryReadPrefix(DraftText, out _, out var remaining) || !string.IsNullOrWhiteSpace(remaining)) &&
        (IsTimeLocked ? lockedLiveTime : clock!.Elapsed) >= TimeSpan.Zero;

    /// <summary>保存状态提示。</summary>
    public string SaveStatus => Localization.Get(IsSaving ? "LiveMarker.Saving" : IsDirty ? "LiveMarker.Unsaved" : "LiveMarker.Saved");

    /// <summary>输入框独立于同传草稿。</summary>
    public string DraftText
    {
        get => draftText;
        set
        {
            var wasEmpty = string.IsNullOrWhiteSpace(draftText);
            if (!SetProperty(ref draftText, value ?? ""))
            {
                return;
            }

            if (!applyingPrefix && HasDocument)
            {
                if (string.IsNullOrWhiteSpace(draftText) && !keepEmptyDraftTime)
                {
                    IsTimeLocked = false;
                }

                if (wasEmpty && !string.IsNullOrWhiteSpace(draftText) && !IsTimeLocked)
                {
                    CaptureInputTime(false);
                }

                if (LiveMarkerTimeInput.TryReadPrefix(draftText, out var time, out var remaining))
                {
                    if (!IsTimeLocked)
                    {
                        CaptureInputTime(true);
                    }

                    lockedLiveTime = time;
                    keepEmptyDraftTime = true;
                    if (remaining.Length > 0 || char.IsWhiteSpace(draftText[^1]))
                    {
                        applyingPrefix = true;
                        DraftText = remaining;
                        applyingPrefix = false;
                    }
                }
            }

            OnPropertyChanged(nameof(HasDraft));
            RefreshClock();
        }
    }

    /// <summary>输入中已锁定的时间，未锁定时显示当前文件时钟。</summary>
    public string InputTimeText => !HasDocument ? "--:--:--" :
        (IsTimeLocked ? lockedLiveTime : clock!.Elapsed) is { } time && time >= TimeSpan.Zero
            ? LiveMarkerTimeInput.Format(time) : Localization.Get("LiveMarker.StartInFuture");

    /// <summary>自动保存开关，默认启用。</summary>
    public bool AutoSaveEnabled
    {
        get => settings?.Invoke().LiveMarkerAutoSaveEnabled ?? true;
        set
        {
            if (settings is null || settings().LiveMarkerAutoSaveEnabled == value)
            {
                return;
            }

            settings().LiveMarkerAutoSaveEnabled = value;
            OnPropertyChanged();
            if (value)
            {
                QueueAutoSave();
            }
            else
            {
                debounce?.Cancel();
            }

            _ = SavePreferencesAsync();
        }
    }

    /// <summary>是否显示只读实际时间列。</summary>
    public bool ShowRealTimeColumn
    {
        get => settings?.Invoke().LiveMarkerShowRealTime ?? true;
        set
        {
            if (settings is null || settings().LiveMarkerShowRealTime == value)
            {
                return;
            }

            settings().LiveMarkerShowRealTime = value;
            OnPropertyChanged();
            _ = SavePreferencesAsync();
        }
    }

    [ObservableProperty] private LiveMarkerEntryViewModel? selectedEntry;
    [ObservableProperty] private LiveMarkerColor selectedColor = LiveMarkerColor.Red;
    [ObservableProperty] private bool isTimeLocked;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isSaving;
    [ObservableProperty] private string lastError = "";

    /// <summary>由应用组合根连接服务，重复配置不替换已打开文档。</summary>
    public void Configure(LiveMarkerServices services, Func<string> roomId, Func<string?> cookie,
        Func<AppSettings> settings, Func<Task> saveState, IAppLogService? log)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (this.services is not null && !ReferenceEquals(this.services, services))
        {
            throw new InvalidOperationException("A live marker session cannot change its file service.");
        }

        this.services = services;
        clock ??= new(services.TimeProvider);
        this.roomId = roomId;
        this.cookie = cookie;
        this.settings = settings;
        this.saveState = saveState;
        this.log = log;
        RefreshState();
    }

    /// <summary>首次激活时恢复该标签的上次文件，读取失败保留关联以便重试。</summary>
    public async Task RestoreAsync()
    {
        if (restoreAttempted || services is null || HasDocument || string.IsNullOrWhiteSpace(state.LiveMarkerFilePath))
        {
            return;
        }

        restoreAttempted = true;
        try
        {
            await OpenAsync(state.LiveMarkerFilePath);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    /// <summary>为本场新建已关联文件，失败时保留当前文档；调用前应完成离开检查。</summary>
    public Task NewAsync(string filePath)
    {
        return ReplaceAsync(filePath, true);
    }

    /// <summary>按文件自己的起点打开文档；调用前应完成离开检查。</summary>
    public Task OpenAsync(string filePath)
    {
        return ReplaceAsync(filePath, false);
    }

    /// <summary>保存当前不可变快照，保存完成不会清除期间产生的新修改。</summary>
    public async Task SaveAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (document is null || services is null)
        {
            return;
        }

        await saveGate.WaitAsync(lifetime.Token);
        try
        {
            IsSaving = true;
            var snapshot = CaptureSnapshot();
            var snapshotNode = node;
            var snapshotGeneration = generation;
            var path = FilePath;
            await services.Files.SaveAsync(snapshot, path, lifetime.Token);
            if (!disposed && snapshotGeneration == generation)
            {
                savedNode = snapshotNode;
                LastError = "";
                RefreshState();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ReportError(exception);
            throw;
        }
        finally
        {
            IsSaving = false;
            saveGate.Release();
        }
    }

    /// <summary>保存到另一文件，成功后才更换当前路径。</summary>
    public async Task SaveAsAsync(string filePath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (document is null || services is null)
        {
            return;
        }

        await operationGate.WaitAsync(lifetime.Token);
        IDisposable? candidateLease = null;
        try
        {
            IsBusy = true;
            await CompletePendingWritesAsync();
            var operationGeneration = generation;
            var path = Path.GetFullPath(filePath);
            candidateLease = services.Files.Acquire(path, this);
            var snapshot = CaptureSnapshot();
            var snapshotNode = node;
            await services.Files.SaveAsync(snapshot, path, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            if (generation != operationGeneration)
            {
                throw new OperationCanceledException();
            }

            fileLease?.Dispose();
            fileLease = candidateLease;
            candidateLease = null;
            state.LiveMarkerFilePath = path;
            savedNode = snapshotNode;
            LastError = "";
            RefreshState();
            await SavePreferencesAsync();
        }
        finally
        {
            candidateLease?.Dispose();
            IsBusy = false;
            operationGate.Release();
        }
    }

    /// <summary>等待已开始的写入，不自动提交新的保存。</summary>
    public async Task CompletePendingWritesAsync()
    {
        debounce?.Cancel();
        await pendingAutoSave;
        await saveGate.WaitAsync();
        saveGate.Release();
    }

    /// <summary>按自动保存设置排空保存工作。</summary>
    public async Task FlushAsync()
    {
        await CompletePendingWritesAsync();
        if (AutoSaveEnabled && IsDirty)
        {
            await SaveAsync();
        }
    }

    /// <summary>在替换、关闭、删除和退出前处理文档与输入草稿。</summary>
    public async Task<bool> PrepareToLeaveAsync(Func<LiveMarkerViewModel, Task<LiveMarkerLeaveDecision>> choose)
    {
        await operationGate.WaitAsync(lifetime.Token);
        operationGate.Release();
        await CompletePendingWritesAsync();
        if (AutoSaveEnabled && IsDirty)
        {
            try
            {
                await SaveAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ReportError(exception);
            }
        }

        if (!IsDirty && !HasDraft)
        {
            return true;
        }

        var decision = await choose(this);
        if (decision == LiveMarkerLeaveDecision.Cancel)
        {
            return false;
        }

        if (decision == LiveMarkerLeaveDecision.Save)
        {
            try
            {
                if (HasDraft)
                {
                    if (!CanAddMarker)
                    {
                        throw new InvalidOperationException(Localization.Get("LiveMarker.InvalidTime"));
                    }

                    AddMarker();
                }

                await SaveAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ReportError(exception);
                return false;
            }
        }

        await CompletePendingWritesAsync();
        return true;
    }

    /// <summary>离开检查通过后解除关联，不删除磁盘文件。</summary>
    public async Task CloseAsync()
    {
        await CompletePendingWritesAsync();
        generation++;
        fileLease?.Dispose();
        fileLease = null;
        document = null;
        state.LiveMarkerFilePath = "";
        Entries.Clear();
        undo.Clear();
        redo.Clear();
        SelectedEntry = null;
        ClearDraft();
        RefreshState();
        await SavePreferencesAsync();
    }

    /// <summary>导出当前已提交快照为文本。</summary>
    public async Task ExportTextAsync(string path)
    {
        using var lease = services!.Files.Acquire(path, new object());
        await services.Exports.ExportTextAsync(CaptureSnapshot(), path, lifetime.Token);
    }

    /// <summary>导出当前已提交快照为带颜色的工作簿。</summary>
    public async Task ExportExcelAsync(string path)
    {
        using var lease = services!.Files.Acquire(path, new object());
        await services.Exports.ExportExcelAsync(CaptureSnapshot(), path, lifetime.Token);
    }

    /// <summary>取得两个时间互不联动的不可变快照。</summary>
    public LiveMarkerDocument CaptureSnapshot()
    {
        return (document ?? throw new InvalidOperationException(Localization.Get("LiveMarker.NoFile"))) with
        {
            Marker = Entries.Select(entry => entry.Snapshot).ToArray()
        };
    }

    /// <summary>刷新面板当前时间，不访问网络。</summary>
    public void RefreshClock()
    {
        OnPropertyChanged(nameof(InputTimeText));
        OnPropertyChanged(nameof(CanAddMarker));
        AddMarkerCommand.NotifyCanExecuteChanged();
    }

    /// <summary>房间身份变化时使未完成的新建取时失效。</summary>
    public void CancelPendingOperation()
    {
        generation++;
        operationCancellation?.Cancel();
    }

    /// <summary>将失败呈现在当前面板及应用日志。</summary>
    public void ReportError(Exception exception)
    {
        LastError = LocalizedExceptionText.Get(exception);
        log?.Error(Localization.Get("LiveMarker.LogSource"), LastError, exception);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
        generation++;
        debounce?.Cancel();
        operationCancellation?.Cancel();
        lifetime.Cancel();
        fileLease?.Dispose();
        fileLease = null;
    }

    [RelayCommand(CanExecute = nameof(CanAddMarker))]
    private void AddMarker()
    {
        if (!CanAddMarker)
        {
            return;
        }

        if (!IsTimeLocked)
        {
            CaptureInputTime(false);
        }

        var entry = new LiveMarkerEntry
        {
            LiveTime = lockedLiveTime,
            RealDateTime = lockedRealTime,
            Message = DraftText.Trim(),
            MarkerColor = SelectedColor
        };
        var edit = new LiveMarkerEdit(Entries.Count, null, entry, node, ++nextNode);
        Entries.Add(CreateRow(entry));
        SelectedEntry = Entries[^1];
        RecordEdit(edit);
        ClearDraft();
    }

    [RelayCommand]
    private void LockTime()
    {
        CaptureInputTime(true);
    }

    private void CaptureInputTime(bool keepWhenEmpty)
    {
        if (!HasDocument || clock is null)
        {
            return;
        }

        lockedLiveTime = clock.Elapsed;
        lockedRealTime = clock.LocalNow;
        keepEmptyDraftTime = keepWhenEmpty;
        IsTimeLocked = true;
        RefreshClock();
    }

    [RelayCommand]
    private void UnlockTime()
    {
        keepEmptyDraftTime = false;
        IsTimeLocked = false;
        RefreshClock();
    }

    private bool CanUndo => CanEdit && undo.Count > 0;
    private bool CanRedo => CanEdit && redo.Count > 0;
    private bool CanDelete => CanEdit && SelectedEntry is not null;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (undo.TryPop(out var edit))
        {
            ApplyEdit(edit, false);
            redo.Push(edit);
            node = edit.BeforeNode;
            Changed();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (redo.TryPop(out var edit))
        {
            ApplyEdit(edit, true);
            undo.Push(edit);
            node = edit.AfterNode;
            Changed();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DeleteSelected()
    {
        if (SelectedEntry is not { } selected)
        {
            return;
        }

        var index = Entries.IndexOf(selected);
        if (index < 0)
        {
            return;
        }

        Entries.RemoveAt(index);
        SelectedEntry = null;
        RecordEdit(new(index, selected.Snapshot, null, node, ++nextNode));
    }

    partial void OnSelectedEntryChanged(LiveMarkerEntryViewModel? value)
    {
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        RefreshState();
    }

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(SaveStatus));
    }

    private async Task ReplaceAsync(string filePath, bool create)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var currentServices = services ?? throw new InvalidOperationException("Live marker services are not configured.");
        await operationGate.WaitAsync(lifetime.Token);
        IDisposable? candidateLease = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operationCancellation = cancellation;
        try
        {
            IsBusy = true;
            await CompletePendingWritesAsync();
            var operationGeneration = generation;
            var path = Path.GetFullPath(filePath);
            candidateLease = currentServices.Files.Acquire(path, this);
            LiveMarkerDocument candidate;
            if (create)
            {
                var start = await currentServices.Clock.CreateStartTimeAsync(roomId!(), cookie!(),
                    TimeSpan.FromSeconds(settings!().TimeoutSeconds), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                candidate = new() { StartTime = start };
                await currentServices.Files.SaveAsync(candidate, path, cancellation.Token);
            }
            else
            {
                candidate = await currentServices.Files.LoadAsync(path, cancellation.Token);
            }

            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != operationGeneration)
            {
                throw new OperationCanceledException();
            }

            generation++;
            fileLease?.Dispose();
            fileLease = candidateLease;
            candidateLease = null;
            document = candidate;
            state.LiveMarkerFilePath = path;
            clock!.Start(candidate.StartTime);
            Entries.Clear();
            foreach (var entry in candidate.Marker)
            {
                Entries.Add(CreateRow(entry));
            }

            undo.Clear();
            redo.Clear();
            node = savedNode = nextNode = 0;
            SelectedEntry = null;
            ClearDraft();
            LastError = "";
            restoreAttempted = true;
            RefreshState();
            await SavePreferencesAsync();
        }
        finally
        {
            operationCancellation = null;
            candidateLease?.Dispose();
            IsBusy = false;
            operationGate.Release();
        }
    }

    private LiveMarkerEntryViewModel CreateRow(LiveMarkerEntry entry)
    {
        return new(entry, ColorOptions, CommitRow);
    }

    private void CommitRow(LiveMarkerEntryViewModel row, LiveMarkerEntry before, LiveMarkerEntry after)
    {
        var index = Entries.IndexOf(row);
        if (index >= 0 && before != after)
        {
            RecordEdit(new(index, before, after, node, ++nextNode));
        }
    }

    private void RecordEdit(LiveMarkerEdit edit)
    {
        undo.Push(edit);
        redo.Clear();
        node = edit.AfterNode;
        Changed();
    }

    private void ApplyEdit(LiveMarkerEdit edit, bool forward)
    {
        var remove = forward ? edit.Before : edit.After;
        var insert = forward ? edit.After : edit.Before;
        if (remove is not null)
        {
            Entries.RemoveAt(edit.Index);
        }

        if (insert is not null)
        {
            Entries.Insert(edit.Index, CreateRow(insert));
            SelectedEntry = Entries[edit.Index];
        }
        else
        {
            SelectedEntry = null;
        }
    }

    private void Changed()
    {
        RefreshState();
        QueueAutoSave();
    }

    private void ClearDraft()
    {
        keepEmptyDraftTime = false;
        applyingPrefix = true;
        DraftText = "";
        applyingPrefix = false;
        IsTimeLocked = false;
        RefreshClock();
    }

    private void QueueAutoSave()
    {
        if (!AutoSaveEnabled || !IsDirty || disposed)
        {
            return;
        }

        debounce?.Cancel();
        debounce = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        pendingAutoSave = AutoSaveAfterDelayAsync(debounce);
    }

    private async Task AutoSaveAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(300, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            await SaveAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
        finally
        {
            if (ReferenceEquals(debounce, cancellation))
            {
                debounce = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task SavePreferencesAsync()
    {
        if (saveState is null || disposed)
        {
            return;
        }

        try
        {
            await saveState();
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void RefreshState()
    {
        foreach (var name in new[] { nameof(HasDocument), nameof(FilePath), nameof(FileName), nameof(StartTimeText),
                     nameof(IsDirty), nameof(SaveStatus), nameof(CanUseFiles), nameof(CanEdit),
                     nameof(AutoSaveEnabled), nameof(ShowRealTimeColumn) })
        {
            OnPropertyChanged(name);
        }

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        RefreshClock();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var color in ColorOptions)
        {
            color.RefreshLanguage();
        }

        foreach (var entry in Entries)
        {
            entry.RefreshLanguage();
        }

        RefreshState();
    }
}
