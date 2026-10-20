using System.Text.Json;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证直播记录路径的本地持久化和工作区分享的边界。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class LiveMarkerApplicationStateTests
{
    /// <summary>各标签文件路径与记录选项能往返保存，相同公开房间也保留各自路径。</summary>
    [Fact]
    public async Task PerTabPathsAndPreferencesRoundTripWithoutPersistingMarkerContents()
    {
        await using var context = new LiveMarkerApplicationTestContext();
        var application = context.Application;
        var first = context.FirstRoom;
        var other = application.Workspaces.Single(workspace => workspace.Id == "workspace-b").Rooms.Single();
        other.RoomId = first.RoomId;
        var firstPath = await context.OpenDirtyAsync(first, "PRIVATE_MARKER_CONTENT_FIRST");
        var otherPath = await context.OpenDirtyAsync(other, "PRIVATE_MARKER_CONTENT_SECOND");
        application.Settings.LiveMarkerShowRealTime = false;

        await application.SaveAsync();
        var restored = new AppStateService(context.DirectoryPath).Load();

        var restoredFirst = restored.Workspaces.Single(workspace => workspace.Id == "workspace-a").LiveRooms
            .Single(room => room.Id == first.Id);
        var restoredOther = restored.Workspaces.Single(workspace => workspace.Id == "workspace-b").LiveRooms.Single();
        Assert.Equal(firstPath, restoredFirst.LiveMarkerFilePath);
        Assert.Equal(otherPath, restoredOther.LiveMarkerFilePath);
        Assert.NotEqual(restoredFirst.LiveMarkerFilePath, restoredOther.LiveMarkerFilePath);
        Assert.Equal(restoredFirst.RoomId, restoredOther.RoomId);
        Assert.False(restored.Settings.LiveMarkerAutoSaveEnabled);
        Assert.False(restored.Settings.LiveMarkerShowRealTime);
        var config = await File.ReadAllTextAsync(context.StateService.WorkspacesFilePath);
        Assert.DoesNotContain("PRIVATE_MARKER_CONTENT_", config);
        Assert.DoesNotContain("RealDateTime", config);
        Assert.DoesNotContain("MarkerColor", config);
    }

    /// <summary>实际分享导出及导入都不包含本地文件路径或已打开记录正文。</summary>
    [Fact]
    public async Task WorkspaceShareExcludesLocalMarkerPathsAndDocumentData()
    {
        await using var context = new LiveMarkerApplicationTestContext();
        var room = context.FirstRoom;
        var markerPath = await context.OpenDirtyAsync(room, "PRIVATE_MARKER_MESSAGE_71C49");
        room.LiveMarker.DraftText = "PRIVATE_MARKER_DRAFT_6B3E1";
        var sharePath = Path.Combine(context.DirectoryPath, "workspace-share.json");

        await context.Application.ExportWorkspaceAsync(context.Workspace, sharePath);
        var serialized = await File.ReadAllTextAsync(sharePath);
        var imported = await new WorkspaceShareService().ImportAsync([], sharePath);

        Assert.DoesNotContain(markerPath, serialized);
        Assert.DoesNotContain(Path.GetFileName(markerPath), serialized);
        Assert.DoesNotContain("LiveMarker", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE_MARKER_", serialized);
        Assert.DoesNotContain("RealDateTime", serialized);
        Assert.All(imported.LiveRooms, importedRoom => Assert.Equal("", importedRoom.LiveMarkerFilePath));
        Assert.Equal(context.Workspace.Rooms.Select(item => item.RoomId), imported.LiveRooms.Select(item => item.RoomId));
        Assert.True(room.LiveMarker.IsDirty);
        Assert.Equal("PRIVATE_MARKER_DRAFT_6B3E1", room.LiveMarker.DraftText);
        Assert.Empty(context.Files.Saves);
    }

    /// <summary>旧配置缺字段时采用默认值，空路径会规范为空字符串。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyConfigurationKeepsDefaultPreferencesAndNormalizesNullPath(bool includeNullPath)
    {
        var directory = Path.Combine(Path.GetTempPath(), "danmu-marker-legacy-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var service = new AppStateService(directory);
            File.WriteAllText(service.SettingsFilePath, "{}");
            var room = includeNullPath
                ? new Dictionary<string, object?> { ["RoomId"] = "100", ["LiveMarkerFilePath"] = null }
                : new Dictionary<string, object?> { ["RoomId"] = "100" };
            File.WriteAllText(service.WorkspacesFilePath, JsonSerializer.Serialize(new
            {
                Workspaces = new[] { new { Name = "旧工作区", LiveRooms = new[] { room } } }
            }));

            var restored = service.Load();

            Assert.True(restored.Settings.LiveMarkerAutoSaveEnabled);
            Assert.True(restored.Settings.LiveMarkerShowRealTime);
            Assert.Equal("", Assert.Single(Assert.Single(restored.Workspaces).LiveRooms).LiveMarkerFilePath);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
