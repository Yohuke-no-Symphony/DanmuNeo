using System.Text.Json;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;

using Yohuke.DanmuNeo.Models.Logging;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 负责读取与保存应用状态。
/// </summary>
public class AppStateService
{
    private const double MIN_SIDEBAR_WIDTH = 200;
    private const double MAX_SIDEBAR_WIDTH = 360;
    private const double MIN_WINDOW_WIDTH = 980;
    private const double MAX_WINDOW_WIDTH = 3840;
    private const double MIN_WINDOW_HEIGHT = 640;
    private const double MAX_WINDOW_HEIGHT = 2400;
    private const double MIN_WORKSPACE_COLUMN_WIDTH = 300;
    private const double MAX_WORKSPACE_COLUMN_WIDTH = 3200;
    private const int MIN_BACKUP_COUNT_PER_FILE = 0;
    private const int MAX_BACKUP_COUNT_PER_FILE = 100;
    private const int DEFAULT_BACKUP_COUNT_PER_FILE = 10;
    private const string LEGACY_STATE_FILE_NAME = "state.json";
    private const string SETTINGS_FILE_NAME = "settings.json";
    private const string ACCOUNTS_FILE_NAME = "accounts.json";
    private const string LYRIC_LIBRARY_FILE_NAME = "lyric-library.json";
    private const string WORKSPACES_FILE_NAME = "workspaces.json";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 配置目录。
    /// </summary>
    public string ConfigDirectory { get; }

    /// <summary>
    /// 旧版整包状态文件路径。
    /// </summary>
    public string StateFilePath { get; }

    /// <summary>
    /// 全局设置文件路径。
    /// </summary>
    public string SettingsFilePath { get; }

    /// <summary>
    /// 账号文件路径。
    /// </summary>
    public string AccountsFilePath { get; }

    /// <summary>
    /// 歌词库文件路径。
    /// </summary>
    public string LyricLibraryFilePath { get; }

    /// <summary>
    /// 工作区文件路径。
    /// </summary>
    public string WorkspacesFilePath { get; }

    /// <summary>
    /// 备份目录。
    /// </summary>
    public string BackupDirectory { get; }

    /// <summary>
    /// 损坏文件留存目录。
    /// </summary>
    public string CorruptDirectory { get; }

    /// <summary>
    /// 初始化状态服务。
    /// </summary>
    public AppStateService(string? configDirectory = null)
    {
        ConfigDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Yohuke.DanmuNeo");
        StateFilePath = Path.Combine(ConfigDirectory, LEGACY_STATE_FILE_NAME);
        SettingsFilePath = Path.Combine(ConfigDirectory, SETTINGS_FILE_NAME);
        AccountsFilePath = Path.Combine(ConfigDirectory, ACCOUNTS_FILE_NAME);
        LyricLibraryFilePath = Path.Combine(ConfigDirectory, LYRIC_LIBRARY_FILE_NAME);
        WorkspacesFilePath = Path.Combine(ConfigDirectory, WORKSPACES_FILE_NAME);
        BackupDirectory = Path.Combine(ConfigDirectory, "Backups");
        CorruptDirectory = Path.Combine(ConfigDirectory, "Corrupt");
    }

    /// <summary>
    /// 同步读取应用状态。
    /// </summary>
    public AppState Load()
    {
        Directory.CreateDirectory(ConfigDirectory);

        if (HasSplitStorage())
        {
            return LoadSplitState();
        }

        if (File.Exists(StateFilePath) || HasBackups(StateFilePath))
        {
            var legacyState = LoadJsonOrDefault(StateFilePath, CreateDefaultState);
            Save(legacyState);
            return legacyState;
        }

        var createdState = CreateDefaultState();
        Save(createdState);
        return createdState;
    }

    /// <summary>
    /// 读取应用状态。
    /// </summary>
    public Task<AppState> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Load());
    }

    /// <summary>
    /// 同步保存应用状态。
    /// </summary>
    public void Save(AppState state)
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Normalize(state);

        WriteJsonAtomic(SettingsFilePath, state.Settings, state.Settings.BackupRetentionCount);
        WriteJsonAtomic(AccountsFilePath, state.Accounts, state.Settings.BackupRetentionCount);
        WriteJsonAtomic(LyricLibraryFilePath, state.LyricLibrary, state.Settings.BackupRetentionCount);
        WriteJsonAtomic(WorkspacesFilePath, new WorkspaceStorageState
        {
            Workspaces = state.Workspaces,
            SelectedWorkspaceId = state.SelectedWorkspaceId
        }, state.Settings.BackupRetentionCount);
    }

    /// <summary>
    /// 保存应用状态。
    /// </summary>
    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Normalize(state);

        await WriteJsonAtomicAsync(SettingsFilePath, state.Settings, state.Settings.BackupRetentionCount, cancellationToken);
        await WriteJsonAtomicAsync(AccountsFilePath, state.Accounts, state.Settings.BackupRetentionCount, cancellationToken);
        await WriteJsonAtomicAsync(
            LyricLibraryFilePath,
            state.LyricLibrary,
            state.Settings.BackupRetentionCount,
            cancellationToken);
        await WriteJsonAtomicAsync(WorkspacesFilePath, new WorkspaceStorageState
        {
            Workspaces = state.Workspaces,
            SelectedWorkspaceId = state.SelectedWorkspaceId
        }, state.Settings.BackupRetentionCount, cancellationToken);
    }

    /// <summary>
    /// 裁剪左侧栏宽度。
    /// </summary>
    public static double ClampSidebarWidth(double width)
    {
        return ClampFinite(width, MIN_SIDEBAR_WIDTH, MAX_SIDEBAR_WIDTH, 240);
    }

    /// <summary>
    /// 裁剪主窗口宽度。
    /// </summary>
    public static double ClampWindowWidth(double width)
    {
        return ClampFinite(width, MIN_WINDOW_WIDTH, MAX_WINDOW_WIDTH, 1280);
    }

    /// <summary>
    /// 裁剪主窗口高度。
    /// </summary>
    public static double ClampWindowHeight(double height)
    {
        return ClampFinite(height, MIN_WINDOW_HEIGHT, MAX_WINDOW_HEIGHT, 820);
    }

    /// <summary>
    /// 裁剪工作区列宽。
    /// </summary>
    public static double ClampWorkspaceColumnWidth(double width)
    {
        return ClampFinite(width, MIN_WORKSPACE_COLUMN_WIDTH, MAX_WORKSPACE_COLUMN_WIDTH, 0);
    }

    /// <summary>
    /// 裁剪自动备份保留数量。
    /// </summary>
    public static int ClampBackupRetentionCount(int count)
    {
        return Math.Min(MAX_BACKUP_COUNT_PER_FILE, Math.Max(MIN_BACKUP_COUNT_PER_FILE, count));
    }

    /// <summary>
    /// 创建默认状态。
    /// </summary>
    public static AppState CreateDefaultState()
    {
        var workspace = new WorkspaceState
        {
            Name = "默认工作区"
        };

        return new()
        {
            Settings = new(),
            Workspaces = [workspace],
            SelectedWorkspaceId = workspace.Id
        };
    }

    private AppState LoadSplitState()
    {
        var workspaceStorage = LoadJsonOrDefault(WorkspacesFilePath, CreateDefaultWorkspaceStorage);
        var state = new AppState
        {
            Settings = LoadJsonOrDefault(SettingsFilePath, () => new AppSettings()),
            Accounts = LoadJsonOrDefault(AccountsFilePath, () => new List<BilibiliAccount>()),
            LyricLibrary = LoadJsonOrDefault(LyricLibraryFilePath, () => new List<LyricLibraryItem>()),
            Workspaces = workspaceStorage.Workspaces,
            SelectedWorkspaceId = workspaceStorage.SelectedWorkspaceId
        };

        return Normalize(state);
    }

    private bool HasSplitStorage()
    {
        return File.Exists(SettingsFilePath) ||
               File.Exists(AccountsFilePath) ||
               File.Exists(LyricLibraryFilePath) ||
               File.Exists(WorkspacesFilePath) ||
               HasBackups(SettingsFilePath) ||
               HasBackups(AccountsFilePath) ||
               HasBackups(LyricLibraryFilePath) ||
               HasBackups(WorkspacesFilePath);
    }

    private T LoadJsonOrDefault<T>(string path, Func<T> createDefault)
    {
        if (TryReadJson(path, out T? value) && value is not null)
        {
            return value;
        }

        if (File.Exists(path))
        {
            PreserveCorruptFile(path);
        }

        foreach (var backupFilePath in EnumerateBackupFiles(path))
        {
            if (!TryReadJson(backupFilePath, out value) || value is null)
            {
                continue;
            }

            RestoreBackup(path, backupFilePath);
            StartupLog.Append(
                $"Recovered state file from backup file={Path.GetFileName(path)} backup={Path.GetFileName(backupFilePath)}",
                AppLogLevel.WARNING, GetDiagnosticText("Services.State.RecoverySource", "Configuration recovery"));
            return value;
        }

        StartupLog.Append($"State file fallback to default file={Path.GetFileName(path)}");
        return createDefault();
    }

    private static bool TryReadJson<T>(string path, out T? value)
    {
        value = default;

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(path);
            value = JsonSerializer.Deserialize<T>(SendingSettingsMigration.Migrate<T>(text), JSON_OPTIONS);
            return value is not null;
        }
        catch (Exception exception)
        {
            StartupLog.Append(
                GetDiagnosticText("Services.State.ReadFailed", "Failed to read configuration: {0}", Path.GetFileName(path)),
                AppLogLevel.ERROR, GetDiagnosticText("Services.State.Source", "Configuration"), exception);
            return false;
        }
    }

    private void WriteJsonAtomic<T>(string path, T value, int backupRetentionCount)
    {
        var tempFilePath = CreateTempFilePath(path);

        try
        {
            var text = JsonSerializer.Serialize(value, JSON_OPTIONS);
            File.WriteAllText(tempFilePath, text);
            BackupExistingFile(path, backupRetentionCount);
            File.Move(tempFilePath, path, true);
        }
        finally
        {
            DeleteTempFile(tempFilePath);
        }
    }

    private async Task WriteJsonAtomicAsync<T>(
        string path,
        T value,
        int backupRetentionCount,
        CancellationToken cancellationToken)
    {
        var tempFilePath = CreateTempFilePath(path);

        try
        {
            await using (var stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, value, JSON_OPTIONS, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            BackupExistingFile(path, backupRetentionCount);
            File.Move(tempFilePath, path, true);
        }
        finally
        {
            DeleteTempFile(tempFilePath);
        }
    }

    private void BackupExistingFile(string path, int backupRetentionCount)
    {
        if (!File.Exists(path) || backupRetentionCount <= 0)
        {
            return;
        }

        Directory.CreateDirectory(BackupDirectory);
        var backupFilePath = Path.Combine(
            BackupDirectory,
            $"{Path.GetFileName(path)}.{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.bak");
        File.Copy(path, backupFilePath, true);
        TrimBackups(path, backupRetentionCount);
    }

    private void PreserveCorruptFile(string path)
    {
        try
        {
            Directory.CreateDirectory(CorruptDirectory);
            var corruptFilePath = Path.Combine(
                CorruptDirectory,
                $"{Path.GetFileName(path)}.{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.corrupt");
            File.Copy(path, corruptFilePath, true);
            StartupLog.Append(
                $"Preserved corrupt state file file={Path.GetFileName(path)} corrupt={Path.GetFileName(corruptFilePath)}");
        }
        catch (Exception exception)
        {
            StartupLog.Append(
                GetDiagnosticText("Services.State.PreserveCorruptFailed", "Failed to preserve corrupt configuration: {0}",
                    Path.GetFileName(path)),
                AppLogLevel.ERROR, GetDiagnosticText("Services.State.Source", "Configuration"), exception);
        }
    }

    private static string GetDiagnosticText(string key, string fallback, params object[] arguments)
    {
        return Localization.IsInitialized ? Localization.Format(key, arguments) : string.Format(fallback, arguments);
    }

    private void RestoreBackup(string path, string backupFilePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ConfigDirectory);
        File.Copy(backupFilePath, path, true);
    }

    private void TrimBackups(string path, int backupRetentionCount)
    {
        var backups = EnumerateBackupFiles(path)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        foreach (var backupFilePath in backups.Skip(backupRetentionCount))
        {
            File.Delete(backupFilePath);
        }
    }

    private IEnumerable<string> EnumerateBackupFiles(string path)
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(BackupDirectory, $"{Path.GetFileName(path)}.*.bak")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenByDescending(filePath => filePath, StringComparer.Ordinal);
    }

    private bool HasBackups(string path)
    {
        return EnumerateBackupFiles(path).Any();
    }

    private static string CreateTempFilePath(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? "";
        return Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
    }

    private static void DeleteTempFile(string tempFilePath)
    {
        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }
    }

    private static WorkspaceStorageState CreateDefaultWorkspaceStorage()
    {
        var state = CreateDefaultState();
        return new()
        {
            Workspaces = state.Workspaces,
            SelectedWorkspaceId = state.SelectedWorkspaceId
        };
    }

    private static AppState Normalize(AppState state)
    {
        state.Settings ??= new();
        state.Accounts ??= [];
        state.LyricLibrary ??= [];
        state.Workspaces ??= [];
        state.Settings.Language = string.IsNullOrWhiteSpace(state.Settings.Language)
            ? "system"
            : state.Settings.Language.Trim();

        if (string.Equals(state.Settings.Language, "system", StringComparison.OrdinalIgnoreCase))
        {
            state.Settings.Language = "system";
        }

        state.Settings.SidebarWidth = ClampSidebarWidth(state.Settings.SidebarWidth);
        state.Settings.WindowWidth = ClampWindowWidth(state.Settings.WindowWidth);
        state.Settings.WindowHeight = ClampWindowHeight(state.Settings.WindowHeight);
        state.Settings.BackupRetentionCount = ClampBackupRetentionCount(state.Settings.BackupRetentionCount);
        state.Settings.WorkspaceLiveColumnWidth =
            ClampWorkspaceColumnWidth(state.Settings.WorkspaceLiveColumnWidth);
        state.Settings.WorkspaceToolColumnWidth =
            ClampWorkspaceColumnWidth(state.Settings.WorkspaceToolColumnWidth);
        MarkSymbolService.Normalize(state.Settings);
        ShieldReplacementService.Normalize(state.Settings);
        ShortcutBindingService.Normalize(state.Settings);

        if (state.Workspaces.Count == 0)
        {
            var workspace = new WorkspaceState();
            state.Workspaces.Add(workspace);
            state.SelectedWorkspaceId = workspace.Id;
        }

        state.Workspaces = state.Workspaces
            .OrderBy(workspace => workspace.SortOrder)
            .ThenBy(workspace => workspace.Name)
            .ToList();

        for (var workspaceIndex = 0; workspaceIndex < state.Workspaces.Count; workspaceIndex++)
        {
            var workspace = state.Workspaces[workspaceIndex];
            workspace.SortOrder = workspaceIndex;

            if (string.IsNullOrWhiteSpace(workspace.Id))
            {
                workspace.Id = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(workspace.Name))
            {
                workspace.Name = "未命名工作区";
            }

            if (string.IsNullOrWhiteSpace(workspace.SelectedMarkGroupId) ||
                state.Settings.MarkGroups.All(group => group.Id != workspace.SelectedMarkGroupId))
            {
                workspace.SelectedMarkGroupId = state.Settings.MarkGroups[0].Id;
            }

            workspace.LiveRooms ??= [];
            workspace.LiveRooms = workspace.LiveRooms
                .OrderBy(room => room.SortOrder)
                .ThenBy(room => room.RoomName)
                .ToList();

            for (var roomIndex = 0; roomIndex < workspace.LiveRooms.Count; roomIndex++)
            {
                var room = workspace.LiveRooms[roomIndex];
                room.SortOrder = roomIndex;

                if (string.IsNullOrWhiteSpace(room.Id))
                {
                    room.Id = Guid.NewGuid().ToString("N");
                }

                room.ForwardRules ??= [];
                room.TranslatedLyricText ??= "";

                if (!Enum.IsDefined(room.LyricSendMode))
                {
                    room.LyricSendMode = LyricSendMode.Bilingual;
                }

                foreach (var rule in room.ForwardRules)
                {
                    if (string.IsNullOrWhiteSpace(rule.Id))
                    {
                        rule.Id = Guid.NewGuid().ToString("N");
                    }

                    if (!string.IsNullOrWhiteSpace(rule.MarkSymbolGroupId) &&
                        state.Settings.MarkGroups.All(group => group.Id != rule.MarkSymbolGroupId))
                    {
                        rule.MarkSymbolGroupId = "";
                    }

                    rule.AccountOverrideId ??= "";
                }
            }

            if (workspace.LiveRooms.Count > 0 &&
                workspace.LiveRooms.All(room => room.Id != workspace.SelectedLiveRoomId))
            {
                workspace.SelectedLiveRoomId = workspace.LiveRooms[0].Id;
            }
        }

        if (state.Workspaces.All(workspace => workspace.Id != state.SelectedWorkspaceId))
        {
            state.SelectedWorkspaceId = state.Workspaces[0].Id;
        }

        if (state.Accounts.Count > 0 && state.Accounts.All(account => !account.IsGlobalDefault))
        {
            state.Accounts[0].IsGlobalDefault = true;
        }

        foreach (var item in state.LyricLibrary)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(item.Source))
            {
                item.Source = "local";
            }

            item.LyricText ??= "";
            item.TranslatedLyricText ??= "";
        }

        return state;
    }

    private static double ClampFinite(double value, double minValue, double maxValue, double defaultValue)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return defaultValue;
        }

        if (value <= 0)
        {
            return defaultValue;
        }

        return Math.Min(maxValue, Math.Max(minValue, value));
    }
}
