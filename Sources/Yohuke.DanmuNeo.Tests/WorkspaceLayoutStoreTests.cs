using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证独立布局文件的持久化、恢复与备份边界。
/// </summary>
public class WorkspaceLayoutStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "danmu-layout-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 首次迁移只写布局文件，后续读取不再应用旧宽度与折叠状态。
    /// </summary>
    [Fact]
    public async Task FirstLoadMigratesOnceWithoutChangingBusinessFiles()
    {
        Directory.CreateDirectory(directory);
        var settingsPath = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{\"existing\":true}");
        var store = new WorkspaceLayoutStore(directory);
        var migrated = await store.LoadAsync(new AppSettings { IsSidebarCollapsed = true, SidebarWidth = 316 });

        Assert.Contains(WorkbenchPanelIds.WORKSPACE, migrated.CurrentLayout.HiddenPanelIds);
        Assert.True(File.Exists(store.FilePath));
        Assert.Equal("{\"existing\":true}", await File.ReadAllTextAsync(settingsPath));

        var reloaded = await new WorkspaceLayoutStore(directory).LoadAsync(new AppSettings());

        Assert.Equal(Serialize(migrated), Serialize(reloaded));
    }

    /// <summary>
    /// 当前布局、用户预设和浮窗完整往返，文件仅保存布局信息。
    /// </summary>
    [Fact]
    public async Task RoundTripPreservesCurrentAndNamedLayouts()
    {
        var store = new WorkspaceLayoutStore(directory);
        var defaultFile = WorkspaceLayoutPresets.CreateDefaultFile();
        var layout = defaultFile.CurrentLayout with
        {
            Main = null,
            FloatingWindows =
            [
                new()
                {
                    Layout = defaultFile.CurrentLayout.Main!,
                    Bounds = new() { X = -1200, Y = 48, Width = 1000, Height = 700, Scaling = 2 }
                }
            ]
        };
        var file = defaultFile with
        {
            CurrentPresetId = "custom-one",
            CurrentLayout = layout,
            UserPresets = [new() { Id = "custom-one", Name = "双屏", Layout = layout }]
        };

        await store.SaveAsync(file);
        var reloaded = await new WorkspaceLayoutStore(directory).LoadAsync();

        Assert.Equal(Serialize(file), Serialize(reloaded));
        Assert.NotSame(file, reloaded);
        Assert.NotSame(file.CurrentLayout.FloatingWindows[0].Layout, reloaded.CurrentLayout.FloatingWindows[0].Layout);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    /// <summary>
    /// 连续保存保留准确旧版本，只裁剪本布局文件的备份。
    /// </summary>
    [Fact]
    public async Task RapidSavesKeepDistinctBackupsAndRespectRetention()
    {
        var retention = 3;
        var store = new WorkspaceLayoutStore(directory, () => retention);
        var first = WorkspaceLayoutPresets.CreateDefaultFile();
        await store.SaveAsync(first);
        Directory.CreateDirectory(store.BackupDirectory);
        var unrelated = Path.Combine(store.BackupDirectory, "settings.json.keep.bak");
        await File.WriteAllTextAsync(unrelated, "settings");

        for (var index = 0; index < 5; index++)
        {
            await store.SaveAsync(CreateNamedFile($"layout-{index}"));
        }

        var backups = Directory.GetFiles(store.BackupDirectory, "layouts.json.*.bak");
        Assert.Equal(3, backups.Length);
        var backupIds = backups.Select(path => JsonDocument.Parse(File.ReadAllText(path)))
            .Select(document =>
            {
                using (document)
                {
                    return document.RootElement.GetProperty("CurrentPresetId").GetString();
                }
            }).ToHashSet();
        Assert.True(backupIds.SetEquals(["layout-1", "layout-2", "layout-3"]));
        Assert.Equal("settings", await File.ReadAllTextAsync(unrelated));

        retention = 0;
        await store.SaveAsync(CreateNamedFile("without-backup"));
        Assert.Equal(3, Directory.GetFiles(store.BackupDirectory, "layouts.json.*.bak").Length);
    }

    /// <summary>
    /// 坏主文件留存，跳过坏备份并原子恢复最新有效备份。
    /// </summary>
    [Fact]
    public async Task CorruptPrimarySkipsInvalidBackupAndRestoresValidLayout()
    {
        var store = new WorkspaceLayoutStore(directory);
        var saved = CreateNamedFile("recover-me");
        await store.SaveAsync(saved);
        await store.SaveAsync(WorkspaceLayoutPresets.CreateDefaultFile());
        var badBackup = Path.Combine(store.BackupDirectory, "layouts.json.newest.bak");
        await File.WriteAllTextAsync(badBackup, "{}");
        File.SetLastWriteTimeUtc(badBackup, DateTime.UtcNow.AddMinutes(1));
        await File.WriteAllTextAsync(store.FilePath, "{ broken");

        var loaded = await store.LoadAsync(new AppSettings { IsSidebarCollapsed = true });

        Assert.Equal(Serialize(saved), Serialize(loaded));
        var corruptFile = Assert.Single(Directory.GetFiles(store.CorruptDirectory, "layouts.json.*.corrupt"));
        Assert.Equal("{ broken", await File.ReadAllTextAsync(corruptFile));
        Assert.Equal(Serialize(saved), Serialize(await new WorkspaceLayoutStore(directory).LoadAsync()));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    /// <summary>
    /// 主文件丢失但有有效备份时，优先恢复而不重新迁移旧配置。
    /// </summary>
    [Fact]
    public async Task MissingPrimaryRestoresBackupBeforeLegacyMigration()
    {
        var store = new WorkspaceLayoutStore(directory);
        var saved = CreateNamedFile("previous");
        await store.SaveAsync(saved);
        await store.SaveAsync(WorkspaceLayoutPresets.CreateDefaultFile());
        File.Delete(store.FilePath);

        var restored = await store.LoadAsync(new AppSettings { IsSidebarCollapsed = true });

        Assert.Equal(Serialize(saved), Serialize(restored));
    }

    /// <summary>
    /// 无法恢复的布局回到新默认排布，不再次套用旧侧栏状态。
    /// </summary>
    [Theory]
    [InlineData("{ broken")]
    [InlineData("{}")]
    public async Task InvalidLayoutFallsBackWithoutReapplyingLegacySettings(string json)
    {
        var store = new WorkspaceLayoutStore(directory);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(store.FilePath, json);

        var loaded = await store.LoadAsync(new AppSettings { IsSidebarCollapsed = true });

        Assert.Equal(Serialize(WorkspaceLayoutPresets.CreateDefaultFile()), Serialize(loaded));
        Assert.Single(Directory.GetFiles(store.CorruptDirectory));
    }

    /// <summary>
    /// 新版本格式明确报错并保留原文件，避免降级覆盖布局。
    /// </summary>
    [Fact]
    public async Task FutureVersionRemainsUntouched()
    {
        var store = new WorkspaceLayoutStore(directory);
        Directory.CreateDirectory(directory);
        const string JSON = "{\"Version\":999,\"FutureLayout\":true}";
        await File.WriteAllTextAsync(store.FilePath, JSON);

        await Assert.ThrowsAsync<NotSupportedException>(() => store.LoadAsync());

        Assert.Equal(JSON, await File.ReadAllTextAsync(store.FilePath));
        Assert.False(Directory.Exists(store.CorruptDirectory));
    }

    /// <summary>
    /// 保存前取消或拒绝非法布局时，原文件和备份不变。
    /// </summary>
    [Fact]
    public async Task CanceledOrInvalidSavePreservesExistingFile()
    {
        var store = new WorkspaceLayoutStore(directory);
        var original = WorkspaceLayoutPresets.CreateDefaultFile();
        await store.SaveAsync(original);
        var contents = await File.ReadAllTextAsync(store.FilePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(
            CreateNamedFile("canceled"), cancellation.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(original with
        {
            CurrentLayout = original.CurrentLayout with { Main = null }
        }));

        Assert.Equal(contents, await File.ReadAllTextAsync(store.FilePath));
        Assert.False(Directory.Exists(store.BackupDirectory));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    /// <summary>
    /// 文件提交失败不会留下临时文件或锁死后续读写。
    /// </summary>
    [Fact]
    public async Task FailedCommitCleansTemporaryFileAndAllowsRetry()
    {
        var store = new WorkspaceLayoutStore(directory);
        Directory.CreateDirectory(store.FilePath);

        await Assert.ThrowsAnyAsync<IOException>(() => store.SaveAsync(WorkspaceLayoutPresets.CreateDefaultFile()));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        Assert.False(Directory.Exists(store.CorruptDirectory));
        Directory.Delete(store.FilePath);

        await store.SaveAsync(CreateNamedFile("retry"));
        Assert.Equal("retry", (await store.LoadAsync()).CurrentPresetId);
    }

    /// <summary>
    /// 已生成备份后的提交失败或取消，只撤销本次备份，原文件和既有备份保持完整。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReplacementRollsBackOnlyItsOwnBackup(bool cancel)
    {
        var originalStore = new WorkspaceLayoutStore(directory, () => 1);
        await originalStore.SaveAsync(CreateNamedFile("previous"));
        await originalStore.SaveAsync(CreateNamedFile("current"));
        var originalJson = await File.ReadAllTextAsync(originalStore.FilePath);
        var previousBackup = Assert.Single(Directory.GetFiles(originalStore.BackupDirectory));
        var previousJson = await File.ReadAllTextAsync(previousBackup);
        using var cancellation = new CancellationTokenSource();
        var shouldFail = true;
        var store = new WorkspaceLayoutStore(directory, () => 1, (source, destination) =>
        {
            if (shouldFail)
            {
                Assert.Equal(2, Directory.GetFiles(originalStore.BackupDirectory).Length);

                if (cancel)
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }

                throw new IOException("测试替换文件失败");
            }

            File.Move(source, destination, true);
        });

        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.SaveAsync(CreateNamedFile("failed"), cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(CreateNamedFile("failed")));
        }

        Assert.Equal(originalJson, await File.ReadAllTextAsync(store.FilePath));
        Assert.Equal(previousBackup, Assert.Single(Directory.GetFiles(store.BackupDirectory)));
        Assert.Equal(previousJson, await File.ReadAllTextAsync(previousBackup));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

        shouldFail = false;
        await store.SaveAsync(CreateNamedFile("retry"));

        Assert.Equal("retry", (await store.LoadAsync()).CurrentPresetId);
        Assert.Equal(originalJson, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(store.BackupDirectory))));
    }

    /// <summary>
    /// 并发读写始终读取完整文件，并能继续提交最后一次保存。
    /// </summary>
    [Fact]
    public async Task ConcurrentReadsAndSavesRemainValid()
    {
        var store = new WorkspaceLayoutStore(directory);
        await store.SaveAsync(WorkspaceLayoutPresets.CreateDefaultFile());
        var operations = Enumerable.Range(0, 12).Select(async index =>
        {
            await store.SaveAsync(CreateNamedFile($"concurrent-{index}"));
            Assert.True(WorkspaceLayoutValidator.TryValidate(await store.LoadAsync(), out var error), error);
        });

        await Task.WhenAll(operations);
        await store.SaveAsync(CreateNamedFile("last"));

        Assert.Equal("last", (await store.LoadAsync()).CurrentPresetId);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    /// <summary>
    /// 全量导出包括布局、备份和损坏留存，布局文件不改变旧业务迁移判断。
    /// </summary>
    [Fact]
    public async Task BackupExportIncludesLayoutsAndLegacyBusinessMigrationStillRuns()
    {
        var store = new WorkspaceLayoutStore(directory);
        await store.LoadAsync();
        await store.SaveAsync(CreateNamedFile("backup"));
        await File.WriteAllTextAsync(store.FilePath, "broken");
        await store.LoadAsync();
        var state = AppStateService.CreateDefaultState();
        state.Workspaces[0].Name = "旧工作区";
        await File.WriteAllTextAsync(Path.Combine(directory, "state.json"), JsonSerializer.Serialize(state));

        Assert.Equal("旧工作区", new AppStateService(directory).Load().Workspaces[0].Name);
        var service = new AppBackupService(new(directory));
        var exported = await service.ExportBackupZipAsync(Path.Combine(directory, "Exports"));
        using var archive = ZipFile.OpenRead(exported);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.Contains("layouts.json", names);
        Assert.Contains("settings.json", names);
        Assert.Contains("workspaces.json", names);
        Assert.Contains(names, name => name.StartsWith("Backups/layouts.json.", StringComparison.Ordinal));
        Assert.Contains(names, name => name.StartsWith("Corrupt/layouts.json.", StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }

        GC.SuppressFinalize(this);
    }

    private static WorkspaceLayoutFile CreateNamedFile(string id)
    {
        var file = WorkspaceLayoutPresets.CreateDefaultFile();
        return file with
        {
            CurrentPresetId = id,
            UserPresets = [new() { Id = id, Name = id, Layout = file.CurrentLayout }]
        };
    }

    private static string Serialize(WorkspaceLayoutFile file)
    {
        return JsonSerializer.Serialize(file);
    }
}
