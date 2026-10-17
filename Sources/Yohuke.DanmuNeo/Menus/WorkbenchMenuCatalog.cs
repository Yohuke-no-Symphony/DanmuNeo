using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Lucide.Avalonia;
using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Menus;

internal sealed class WorkbenchMenuCatalog : IDisposable
{
    private readonly WorkbenchLayoutController layouts;
    private readonly Dictionary<string, RelayCommand> panelCommands = [];
    private readonly Dictionary<string, RelayCommand> layoutCommands = [];
    private readonly List<IRelayCommand> commands = [];
    private readonly Func<bool> isModal;
    private readonly RelayCommand saveLayout;
    private readonly RelayCommand saveAsLayout;
    private readonly RelayCommand manageLayouts;
    private readonly RelayCommand restoreDefault;
    private readonly RelayCommand about;
    private readonly RelayCommand update;

    internal WorkbenchMenuCatalog(WorkbenchLayoutController layouts, Action settings, Action saveAs,
        Action manage, Action showAbout, Action checkUpdate, Func<bool> isModal)
    {
        this.layouts = layouts;
        this.isModal = isModal;
        SettingsCommand = new(settings);
        saveLayout = CreateCommand(() => layouts.SaveCurrent(), () => !isModal() && IsCustomLayout);
        saveAsLayout = CreateCommand(saveAs);
        manageLayouts = CreateCommand(manage);
        restoreDefault = CreateCommand(layouts.RestoreDefault);
        about = CreateCommand(showAbout);
        update = CreateCommand(checkUpdate);

        foreach (var id in WorkbenchPanelIds.All)
        {
            panelCommands.Add(id, CreateCommand(() =>
            {
                layouts.ShowPanel(id);
                Refresh();
            }));
        }

        layouts.Changed += OnLayoutChanged;
    }

    internal event Action? Changed;
    internal RelayCommand SettingsCommand { get; }
    internal bool IsCustomLayout => !WorkspaceLayoutPresets.BuiltIn.Any(preset => preset.Id == layouts.CurrentPresetId);

    internal IReadOnlyList<WorkbenchMenuEntry> Entries
    {
        get
        {
            var layoutEntries = new List<WorkbenchMenuEntry>();
            var builtInIds = WorkspaceLayoutPresets.BuiltIn.Select(preset => preset.Id).ToHashSet(StringComparer.Ordinal);
            var hasUserPresets = false;

            foreach (var preset in layouts.Presets)
            {
                if (!builtInIds.Contains(preset.Id) && !hasUserPresets)
                {
                    layoutEntries.Add(new("separator:layout-users", "", IsSeparator: true));
                    hasUserPresets = true;
                }

                if (!layoutCommands.TryGetValue(preset.Id, out var command))
                {
                    command = CreateCommand(() => layouts.ApplyPreset(preset.Id));
                    layoutCommands.Add(preset.Id, command);
                }

                layoutEntries.Add(new($"layout:{preset.Id}", preset.Name, GetLayoutIcon(preset.Id), command,
                    layouts.CurrentPresetId == preset.Id, true));
            }

            layoutEntries.AddRange([
                new("separator:layout-actions", "", IsSeparator: true),
                new("layout-save", "保存当前布局", LucideIconKind.Save, saveLayout),
                new("layout-save-as", "另存为布局…", LucideIconKind.SaveAll, saveAsLayout),
                new("layout-manage", "管理布局…", LucideIconKind.SlidersHorizontal, manageLayouts),
                new("layout-default", "恢复默认布局", LucideIconKind.RotateCcw, restoreDefault)
            ]);
            return
            [
                new("application", "应用", Children:
                [new("settings", "设置…", LucideIconKind.Settings, SettingsCommand, Gesture: new(Key.OemComma,
                    OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))]),
                new("views", "视图", Children: WorkbenchPanelIds.All.Select(id =>
                    new WorkbenchMenuEntry($"panel:{id}", GetPanelTitle(id), GetPanelIcon(id), panelCommands[id])).ToArray()),
                new("layouts", "布局", Children: layoutEntries),
                new("help", "帮助", Children:
                [new("about", "关于", LucideIconKind.Info, about), new("update", "检查更新", LucideIconKind.Download, update)])
            ];
        }
    }

    internal static string GetPanelTitle(string id)
    {
        return id switch
        {
            WorkbenchPanelIds.WORKSPACE => "工作区",
            WorkbenchPanelIds.LIVE_PLAYER => "追帧",
            WorkbenchPanelIds.COMPOSER => "输入框",
            WorkbenchPanelIds.SUPER_CHAT => "SC",
            WorkbenchPanelIds.DANMU => "弹幕",
            WorkbenchPanelIds.HISTORY => "历史",
            WorkbenchPanelIds.LYRICS => "歌词",
            WorkbenchPanelIds.FORWARDING => "转发",
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
    }

    private static LucideIconKind GetPanelIcon(string id)
    {
        return id switch
        {
            WorkbenchPanelIds.WORKSPACE => LucideIconKind.FolderTree,
            WorkbenchPanelIds.LIVE_PLAYER => LucideIconKind.MonitorPlay,
            WorkbenchPanelIds.COMPOSER => LucideIconKind.SquarePen,
            WorkbenchPanelIds.SUPER_CHAT => LucideIconKind.BadgeDollarSign,
            WorkbenchPanelIds.DANMU => LucideIconKind.MessagesSquare,
            WorkbenchPanelIds.HISTORY => LucideIconKind.History,
            WorkbenchPanelIds.LYRICS => LucideIconKind.Music,
            WorkbenchPanelIds.FORWARDING => LucideIconKind.Forward,
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
    }

    private static LucideIconKind GetLayoutIcon(string id)
    {
        return id switch
        {
            WorkspaceLayoutPresets.DEFAULT => LucideIconKind.PanelsTopLeft,
            WorkspaceLayoutPresets.TRANSLATION => LucideIconKind.Columns3,
            WorkspaceLayoutPresets.LYRICS => LucideIconKind.ListMusic,
            WorkspaceLayoutPresets.FORWARDING => LucideIconKind.Share2,
            _ => LucideIconKind.LayoutTemplate
        };
    }

    internal void Refresh()
    {
        foreach (var command in commands)
        {
            command.NotifyCanExecuteChanged();
        }

        Changed?.Invoke();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        layouts.Changed -= OnLayoutChanged;
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private RelayCommand CreateCommand(Action action, Func<bool>? canExecute = null)
    {
        var command = new RelayCommand(action, canExecute ?? (() => !isModal()));
        commands.Add(command);
        return command;
    }
}
