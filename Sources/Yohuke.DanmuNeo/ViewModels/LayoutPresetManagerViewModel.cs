using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
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
            Execute(() => controller.ApplyPreset(row.Id), $"已应用布局：{row.Name}");
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
                throw new ArgumentException("找不到指定的自定义布局。");
            }

            Name = Selected?.Name ?? string.Empty;
        }, "布局已重命名");
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
        }, "当前布局已另存为预设");
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
                        throw new ArgumentException("所选布局已不存在，或包含不可删除的内置布局。");
                    }
                }, $"已删除 {ids.Length} 个布局预设");
            }
        }
        catch (OperationCanceledException) when (disposed)
        {
        }
        catch (Exception exception)
        {
            Error = exception.Message;
            log?.Error("布局管理", "删除布局确认失败", exception);
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
            log?.Information("布局管理", successMessage);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Error = exception.Message;
            log?.Error("布局管理", "布局操作失败", exception);
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

    private void OnPersistenceFailed(Exception exception)
    {
        Error = exception.Message;
    }
}
