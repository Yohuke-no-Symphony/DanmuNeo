using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.Models.State;
using System.Text.Json;

namespace Yohuke.DanmuNeo.Tests;

public class AppStateServiceTests
{
    [Fact]
    public void DefaultConfigDirectoryUsesApplicationData()
    {
        var service = new AppStateService();
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Yohuke.DanmuNeo");

        Assert.Equal(expected, service.ConfigDirectory);
    }

    [Fact]
    public async Task LoadCreatesDefaultStateWhenFileMissing()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);

        var state = await service.LoadAsync();

        Assert.Single(state.Workspaces);
        Assert.Equal("默认工作区", state.Workspaces[0].Name);
        Assert.Equal(Yohuke.DanmuNeo.Models.State.AppThemeMode.System, state.Settings.ThemeMode);
        Assert.False(state.Settings.CompactDanmuDisplay);
        Assert.False(state.Settings.HideEmoticonDanmu);
        Assert.True(File.Exists(service.SettingsFilePath));
        Assert.True(File.Exists(service.AccountsFilePath));
        Assert.True(File.Exists(service.LyricLibraryFilePath));
        Assert.True(File.Exists(service.WorkspacesFilePath));
    }

    [Fact]
    public async Task LoadFallsBackToDefaultStateWhenJsonIsBroken()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var service = new AppStateService(directory);
        await File.WriteAllTextAsync(service.WorkspacesFilePath, "{ broken json");

        var state = await service.LoadAsync();

        Assert.Single(state.Workspaces);
        Assert.Equal("默认工作区", state.Workspaces[0].Name);
        Assert.True(Directory.EnumerateFiles(service.CorruptDirectory).Any());
    }

    [Theory]
    [InlineData(120, 200)]
    [InlineData(240, 240)]
    [InlineData(600, 360)]
    public void ClampSidebarWidthKeepsConfiguredRange(double input, double expected)
    {
        Assert.Equal(expected, AppStateService.ClampSidebarWidth(input));
    }

    [Fact]
    public void SaveAndLoadNormalizesLayoutSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var state = new AppState
        {
            Settings = new()
            {
                SidebarWidth = 999,
                WindowWidth = 100,
                WindowHeight = 9999,
                WorkspaceLiveColumnWidth = 250,
                WorkspaceToolColumnWidth = 500
            }
        };

        service.Save(state);
        var loaded = service.Load();

        Assert.Equal(360, loaded.Settings.SidebarWidth);
        Assert.Equal(980, loaded.Settings.WindowWidth);
        Assert.Equal(2400, loaded.Settings.WindowHeight);
        Assert.Equal(300, loaded.Settings.WorkspaceLiveColumnWidth);
        Assert.Equal(500, loaded.Settings.WorkspaceToolColumnWidth);
        Assert.Equal(ShortcutActionCatalog.Actions.Count, loaded.Settings.ShortcutBindings.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveAndLoadKeepsSidebarLayout(bool isCollapsed)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var service = new AppStateService(directory);
            service.Save(new()
            {
                Settings = new() { IsSidebarCollapsed = isCollapsed, SidebarWidth = 316 }
            });

            var loaded = service.Load();

            Assert.Equal(isCollapsed, loaded.Settings.IsSidebarCollapsed);
            Assert.Equal(316, loaded.Settings.SidebarWidth);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void LoadSettingsWithoutSidebarStateKeepsExpandedLayout()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(directory);
            var service = new AppStateService(directory);
            File.WriteAllText(service.SettingsFilePath, """{"SidebarWidth":316}""");

            var loaded = service.Load();

            Assert.False(loaded.Settings.IsSidebarCollapsed);
            Assert.Equal(316, loaded.Settings.SidebarWidth);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SaveAndLoadNormalizesShortcutBindings()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var state = new AppState
        {
            Settings = new()
            {
                ShortcutBindings =
                [
                    new()
                    {
                        ActionKey = ShortcutActionKeys.LIVE_PLAYER_CHASE,
                        GestureText = "NotAKey",
                        IsEnabled = true
                    },
                    new()
                    {
                        ActionKey = ShortcutActionKeys.INPUT_CLEAR_DRAFT,
                        GestureText = "",
                        IsEnabled = false
                    }
                ]
            }
        };

        service.Save(state);
        var loaded = service.Load();
        var chaseBinding = loaded.Settings.ShortcutBindings
            .Single(binding => binding.ActionKey == ShortcutActionKeys.LIVE_PLAYER_CHASE);
        var clearBinding = loaded.Settings.ShortcutBindings
            .Single(binding => binding.ActionKey == ShortcutActionKeys.INPUT_CLEAR_DRAFT);
        var defaultGestureText = ShortcutActionCatalog.Actions
            .Single(action => action.ActionKey == ShortcutActionKeys.LIVE_PLAYER_CHASE)
            .DefaultGestureText;

        Assert.Equal(defaultGestureText, chaseBinding.GestureText);
        Assert.Equal("", clearBinding.GestureText);
        Assert.False(clearBinding.IsEnabled);
        Assert.Equal(ShortcutActionCatalog.Actions.Count, loaded.Settings.ShortcutBindings.Count);
    }

    [Fact]
    public void LoadAddsShortcutBindingsWhenOldSettingsFileDoesNotHaveThem()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var service = new AppStateService(directory);
        File.WriteAllText(service.SettingsFilePath, """{"SendIntervalMs":650}""");

        var loaded = service.Load();

        Assert.Equal(650, loaded.Settings.SendIntervalMs);
        Assert.Equal(ShortcutActionCatalog.Actions.Count, loaded.Settings.ShortcutBindings.Count);
        var defaultGestureText = ShortcutActionCatalog.Actions
            .Single(action => action.ActionKey == ShortcutActionKeys.LYRIC_SEEK_BACKWARD)
            .DefaultGestureText;
        Assert.Contains(
            loaded.Settings.ShortcutBindings,
            binding => binding.ActionKey == ShortcutActionKeys.LYRIC_SEEK_BACKWARD &&
                       binding.GestureText == defaultGestureText);
    }

    [Fact]
    public void LoadUsesNormalDanmuDisplayWhenOldSettingsFileDoesNotContainOption()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var service = new AppStateService(directory);
        File.WriteAllText(service.SettingsFilePath, """{"SendIntervalMs":650}""");

        var loaded = service.Load();

        Assert.False(loaded.Settings.CompactDanmuDisplay);
        Assert.False(loaded.Settings.HideEmoticonDanmu);
    }

    [Fact]
    public void SaveAndLoadKeepsCompactDanmuDisplay()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var state = new AppState
        {
            Settings = new()
            {
                CompactDanmuDisplay = true
            }
        };

        service.Save(state);
        var loaded = service.Load();

        Assert.True(loaded.Settings.CompactDanmuDisplay);
    }

    [Fact]
    public void SaveAndLoadKeepsHideEmoticonDanmu()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var state = new AppState
        {
            Settings = new()
            {
                HideEmoticonDanmu = true
            }
        };

        service.Save(state);
        var loaded = service.Load();

        Assert.True(loaded.Settings.HideEmoticonDanmu);
    }

    [Fact]
    public void SaveAndLoadKeepsWorkspaceAndRoomSortOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var state = new AppState
        {
            Workspaces =
            [
                new()
                {
                    Name = "B",
                    SortOrder = 1,
                    LiveRooms =
                    [
                        new()
                        {
                            RoomName = "B2",
                            SortOrder = 1
                        },
                        new()
                        {
                            RoomName = "B1",
                            SortOrder = 0
                        }
                    ]
                },
                new()
                {
                    Name = "A",
                    SortOrder = 0
                }
            ]
        };

        service.Save(state);
        var loaded = service.Load();

        Assert.Equal("A", loaded.Workspaces[0].Name);
        Assert.Equal("B", loaded.Workspaces[1].Name);
        Assert.Equal("B1", loaded.Workspaces[1].LiveRooms[0].RoomName);
        Assert.Equal("B2", loaded.Workspaces[1].LiveRooms[1].RoomName);
    }

    [Fact]
    public void LoadMigratesLegacyStateFileToSplitStorage()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var service = new AppStateService(directory);
        var legacyState = new AppState
        {
            Workspaces =
            [
                new()
                {
                    Name = "旧工作区"
                }
            ],
            LyricLibrary =
            [
                new()
                {
                    Title = "旧歌词",
                    LyricText = "[00:01.00]test"
                }
            ]
        };
        File.WriteAllText(service.StateFilePath, JsonSerializer.Serialize(legacyState));

        var state = service.Load();

        Assert.Equal("旧工作区", state.Workspaces[0].Name);
        Assert.Equal("旧歌词", state.LyricLibrary[0].Title);
        Assert.True(File.Exists(service.SettingsFilePath));
        Assert.True(File.Exists(service.LyricLibraryFilePath));
        Assert.True(File.Exists(service.WorkspacesFilePath));
    }

    [Fact]
    public void SaveCreatesBackupBeforeReplacingSplitFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var firstState = new AppState
        {
            Workspaces =
            [
                new()
                {
                    Name = "第一次保存"
                }
            ]
        };
        var secondState = new AppState
        {
            Workspaces =
            [
                new()
                {
                    Name = "第二次保存"
                }
            ]
        };

        service.Save(firstState);
        service.Save(secondState);

        var backupFile = Directory
            .EnumerateFiles(service.BackupDirectory, "workspaces.json.*.bak")
            .Single();
        var backupState = JsonSerializer.Deserialize<WorkspaceStorageState>(File.ReadAllText(backupFile));

        Assert.Equal("第一次保存", backupState?.Workspaces[0].Name);
        Assert.Equal("第二次保存", service.Load().Workspaces[0].Name);
    }

    [Fact]
    public void SaveTrimsBackupsByConfiguredRetentionCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var state = new AppState
        {
            Settings = new()
            {
                BackupRetentionCount = 1
            },
            Workspaces =
            [
                new()
                {
                    Name = "第一次保存"
                }
            ]
        };

        service.Save(state);

        for (var index = 0; index < 3; index++)
        {
            state.Workspaces[0].Name = $"第 {index + 2} 次保存";
            System.Threading.Thread.Sleep(5);
            service.Save(state);
        }

        Assert.Single(Directory.EnumerateFiles(service.BackupDirectory, "settings.json.*.bak"));
        Assert.Single(Directory.EnumerateFiles(service.BackupDirectory, "accounts.json.*.bak"));
        Assert.Single(Directory.EnumerateFiles(service.BackupDirectory, "lyric-library.json.*.bak"));
        Assert.Single(Directory.EnumerateFiles(service.BackupDirectory, "workspaces.json.*.bak"));
    }

    [Fact]
    public void LoadRestoresWorkspaceFileFromLatestValidBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var state = new AppState
        {
            Workspaces =
            [
                new()
                {
                    Name = "可恢复工作区"
                }
            ]
        };

        service.Save(state);
        service.Save(state);
        File.WriteAllText(service.WorkspacesFilePath, "{ broken json");

        var loaded = service.Load();

        Assert.Equal("可恢复工作区", loaded.Workspaces[0].Name);
        var restoredState =
            JsonSerializer.Deserialize<WorkspaceStorageState>(File.ReadAllText(service.WorkspacesFilePath));
        Assert.Equal("可恢复工作区", restoredState?.Workspaces[0].Name);
    }

    [Fact]
    public void BrokenLyricLibraryDoesNotDropWorkspaces()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var state = new AppState
        {
            Workspaces =
            [
                new()
                {
                    Name = "工作区仍在"
                }
            ],
            LyricLibrary =
            [
                new()
                {
                    Title = "歌词",
                    LyricText = "[00:01.00]test"
                }
            ]
        };

        service.Save(state);
        File.WriteAllText(service.LyricLibraryFilePath, "{ broken json");

        var loaded = service.Load();

        Assert.Equal("工作区仍在", loaded.Workspaces[0].Name);
        Assert.Empty(loaded.LyricLibrary);
    }

    [Fact]
    public void SaveAndLoadKeepsForwardRules()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var state = new AppState
        {
            Settings = new()
            {
                MarkGroups =
                [
                    new()
                    {
                        Id = "mark-group",
                        Name = "转发符号组"
                    }
                ]
            },
            Workspaces =
            [
                new()
                {
                    Name = "转发工作区",
                    LiveRooms =
                    [
                        new()
                        {
                            RoomId = "100",
                            RoomName = "目标",
                            OwnerUid = "10001",
                            ForwardRules =
                            [
                                new()
                                {
                                    SourceWorkspaceId = "source-workspace",
                                    SourceRoomStateId = "source-room",
                                    MarkSymbolGroupId = "mark-group",
                                    AccountOverrideId = "forward-account",
                                    SenderUid = "42",
                                    ContentPattern = "^【.+】$",
                                    IsEnabled = true
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        service.Save(state);
        var loaded = service.Load();
        var rule = loaded.Workspaces[0].LiveRooms[0].ForwardRules[0];

        Assert.Equal("10001", loaded.Workspaces[0].LiveRooms[0].OwnerUid);
        Assert.True(rule.IsEnabled);
        Assert.Equal("source-workspace", rule.SourceWorkspaceId);
        Assert.Equal("source-room", rule.SourceRoomStateId);
        Assert.Equal("mark-group", rule.MarkSymbolGroupId);
        Assert.Equal("forward-account", rule.AccountOverrideId);
        Assert.Equal("42", rule.SenderUid);
        Assert.Equal("^【.+】$", rule.ContentPattern);
    }

    [Fact]
    public void SaveAndLoadNormalizesUnknownLyricSendMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var service = new AppStateService(directory);
        var state = new AppState
        {
            Workspaces =
            [
                new()
                {
                    LiveRooms =
                    [
                        new()
                        {
                            LyricSendMode = (LyricSendMode)999
                        }
                    ]
                }
            ]
        };

        service.Save(state);
        var loaded = service.Load();

        Assert.Equal(LyricSendMode.Bilingual, loaded.Workspaces[0].LiveRooms[0].LyricSendMode);
    }
}
