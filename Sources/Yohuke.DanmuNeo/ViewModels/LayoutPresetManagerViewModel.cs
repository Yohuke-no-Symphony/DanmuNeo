using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.ViewModels;

internal sealed class LayoutPresetManagerViewModel : ViewModelBase, IDisposable
{
    private readonly WorkbenchLayoutController controller;
    private readonly IAppLogService? log;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<bool>> confirmDeletion;
    private readonly CancellationTokenSource cancellation = new();
    private LayoutPresetRow? selected;
    private ImmutableArray<string> selectedIds = [];
    private string name = string.Empty;
    private string? error;
    private Exception? errorSource;
    private bool isBusy;
    private bool disposed;

    internal LayoutPresetManagerViewModel(WorkbenchLayoutController controller,
        Func<IReadOnlyList<string>, CancellationToken, Task<bool>> confirmDeletion,
        IAppLogService? log = null)
    {
        this.controller = controller;
        this.confirmDeletion = confirmDeletion;
        this.log = log;
        ApplyCommand = new(Apply, () => CanEdit && SelectedIds.Length == 1);
        RenameCommand = new(Rename, () => CanEdit && SelectedIds.Length == 1 && Selected is { IsReadOnly: false });
        DeleteCommand = new(DeleteAsync, CanDelete);
        SaveAsCommand = new(SaveAs, () => CanEdit);
        controller.Changed += OnControllerChanged;
        controller.PersistenceFailed += OnPersistenceFailed;
        Refresh();
        Localization.LanguageChanged += OnLanguageChanged;
    }

    internal event EventHandler? ChoicesRefreshing;
    internal event EventHandler? ChoicesRefreshed;
    public ObservableCollection<LayoutPresetRow> Presets { get; } = [];
    public RelayCommand ApplyCommand { get; }
    public RelayCommand RenameCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public ImmutableArray<string> SelectedIds => selectedIds;
    public bool CanEdit => !isBusy && !disposed;
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public LayoutPresetRow? Selected
    {
        get => selected;
        set => SetSelection(value?.Id, value is null ? [] : [value.Id]);
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value))
            {
                Error = null;
            }
        }
    }

    public string? Error
    {
        get => error;
        private set
        {
            if (value is null)
            {
                errorSource = null;
            }

            if (SetProperty(ref error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    internal void SetSelection(string? primaryId, IEnumerable<string> ids)
    {
        if (!CanEdit)
        {
            return;
        }

        var requested = ids.ToHashSet(StringComparer.Ordinal);
        UpdateSelection(primaryId, Presets.Where(row => requested.Contains(row.Id))
            .Select(row => row.Id).ToImmutableArray());
    }

    /// <summary>
    /// 关闭管理器时解除布局订阅并取消待完成的删除确认。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
        controller.Changed -= OnControllerChanged;
        controller.PersistenceFailed -= OnPersistenceFailed;
        cancellation.Cancel();
        cancellation.Dispose();
        NotifyCommands();
    }

    private bool CanDelete()
    {
        return CanEdit && SelectedIds.Length > 0 &&
               SelectedIds.All(id => Presets.Any(row => row.Id == id && !row.IsReadOnly));
    }

    private void Apply()
    {
        if (CanEdit && SelectedIds.Length == 1 && Selected is { } row)
        {
            Execute(() => controller.ApplyPreset(row.Id), Localization.Format("Layout.Applied", row.Name));
        }
    }

    private void Rename()
    {
        if (!CanEdit || SelectedIds.Length != 1 || Selected is not { IsReadOnly: false } row)
        {
            return;
        }

        Execute(() =>
        {
            if (!controller.Rename(row.Id, Name))
            {
                throw LayoutValidationFailure.CreateException("Layout.CustomPresetMissing");
            }

            Name = Selected?.Name ?? string.Empty;
        }, Localization.Get("Layout.Renamed"));
    }

    private void SaveAs()
    {
        if (!CanEdit)
        {
            return;
        }

        Execute(() =>
        {
            var id = controller.SaveAs(Name);
            SetSelection(id, [id]);
        }, Localization.Get("Layout.SavedAs"));
    }

    private async Task DeleteAsync()
    {
        if (!CanDelete())
        {
            return;
        }

        var ids = SelectedIds;
        var names = Presets.Where(row => ids.Contains(row.Id)).Select(row => row.Name).ToArray();
        var token = cancellation.Token;
        isBusy = true;
        NotifyCommands();

        try
        {
            if (!await confirmDeletion(names, token).WaitAsync(token))
            {
                return;
            }

            token.ThrowIfCancellationRequested();

            if (!disposed)
            {
                Execute(() =>
                {
                    if (!controller.Delete(ids))
                    {
                        throw LayoutValidationFailure.CreateException("Layout.DeleteSelectionInvalid");
                    }
                }, Localization.Format("Layout.DeletedCount", ids.Length));
            }
        }
        catch (OperationCanceledException) when (disposed)
        {
        }
        catch (Exception exception)
        {
            ReportError(exception);
            log?.Error(Localization.Get("Window.ManageLayouts.Title"), Localization.Get("Layout.DeleteConfirmationFailed"), exception);
        }
        finally
        {
            isBusy = false;

            if (!disposed)
            {
                NotifyCommands();
            }
        }
    }

    private void Execute(Action action, string successMessage)
    {
        try
        {
            action();
            Error = null;
            log?.Information(Localization.Get("Window.ManageLayouts.Title"), successMessage);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ReportError(exception);
            log?.Error(Localization.Get("Window.ManageLayouts.Title"), Localization.Get("Layout.OperationFailed"), exception);
        }
    }

    private void UpdateSelection(string? primaryId, ImmutableArray<string> ids)
    {
        selectedIds = ids;
        var row = Presets.FirstOrDefault(value => value.Id == primaryId && ids.Contains(value.Id)) ??
                  Presets.FirstOrDefault(value => ids.Contains(value.Id));

        if (!ReferenceEquals(selected, row))
        {
            selected = row;
            OnPropertyChanged(nameof(Selected));
            Name = row is { IsReadOnly: false } ? row.Name : string.Empty;
        }

        OnPropertyChanged(nameof(SelectedIds));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanEdit));
        ApplyCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
    }

    private void Refresh()
    {
        var draftName = Name;
        var previousError = Error;
        var previousErrorSource = errorSource;
        var previousId = Selected?.Id;
        var ids = SelectedIds;
        var selectedId = previousId ?? controller.CurrentPresetId;
        var builtInIds = WorkspaceLayoutPresets.BuiltIn.Select(preset => preset.Id).ToHashSet(StringComparer.Ordinal);
        ChoicesRefreshing?.Invoke(this, EventArgs.Empty);

        try
        {
            Presets.Clear();

            foreach (var preset in controller.Presets)
            {
                Presets.Add(new(preset.Id, preset.Name, builtInIds.Contains(preset.Id)));
            }

            var retained = Presets.Where(row => ids.Contains(row.Id)).Select(row => row.Id).ToImmutableArray();

            if (retained.IsEmpty)
            {
                var fallback = Presets.FirstOrDefault(row => row.Id == selectedId) ??
                               Presets.FirstOrDefault(row => row.Id == controller.CurrentPresetId) ??
                               Presets.FirstOrDefault();
                selectedId = fallback?.Id;
                retained = fallback is null ? [] : [fallback.Id];
            }

            UpdateSelection(selectedId, retained);

            if (Selected?.Id == previousId)
            {
                Name = draftName;
                Error = previousError;
                errorSource = previousErrorSource;
            }
        }
        finally
        {
            ChoicesRefreshed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnControllerChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var row in Presets)
        {
            row.RefreshLocalization();
        }

        if (errorSource is not null)
        {
            Error = LayoutValidationFailure.GetDisplayMessage(errorSource);
        }
    }

    private void OnPersistenceFailed(Exception exception)
    {
        ReportError(exception);
    }

    private void ReportError(Exception exception)
    {
        errorSource = exception;
        Error = LayoutValidationFailure.GetDisplayMessage(exception);
    }
}
