using Yohuke.DanmuNeo.Models.LiveMarker;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证删除和关闭预检尊重记录文档的保存选择及房间生命周期。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class LiveMarkerApplicationLifecycleTests
{
    /// <summary>取消删除房间或工作区时保留原实例、配置和未保存的记录与草稿。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelDeletionKeepsRoomWorkspaceStateAndUnsavedData(bool deleteWorkspace)
    {
        await using var context = new LiveMarkerApplicationTestContext();
        var application = context.Application;
        var workspace = context.Workspace;
        var room = context.FirstRoom;
        var path = await context.OpenDirtyAsync(room, "尚未保存的打点");
        room.LiveMarker.DraftText = "尚未提交的草稿";
        var persisted = await File.ReadAllBytesAsync(context.StateService.WorkspacesFilePath);
        var confirmations = 0;
        application.ConfirmLiveMarkerLeaveAsync = marker =>
        {
            Assert.Same(room.LiveMarker, marker);
            confirmations++;
            return Task.FromResult(LiveMarkerLeaveDecision.Cancel);
        };

        if (deleteWorkspace)
        {
            await application.DeleteWorkspaceAsync(workspace);
        }
        else
        {
            await application.RemoveRoomAsync(room);
        }

        Assert.Equal(1, confirmations);
        Assert.Contains(workspace, application.Workspaces);
        Assert.Contains(workspace.State, application.State.Workspaces);
        Assert.Contains(room, workspace.Rooms);
        Assert.Contains(room.State, workspace.State.LiveRooms);
        Assert.True(room.LiveMarker.CanEdit);
        Assert.True(room.LiveMarker.IsDirty);
        Assert.Equal(path, room.State.LiveMarkerFilePath);
        Assert.Equal("尚未保存的打点", Assert.Single(room.LiveMarker.Entries).Message);
        Assert.Equal("尚未提交的草稿", room.LiveMarker.DraftText);
        Assert.Empty(context.Files.Saves);
        Assert.Equal(persisted, await File.ReadAllBytesAsync(context.StateService.WorkspacesFilePath));
    }

    /// <summary>工作区后续文档取消时，前面选择放弃的房间也保持可编辑和原始未保存内容。</summary>
    [Fact]
    public async Task CancelingLaterDocumentDoesNotDisposeEarlierRooms()
    {
        await using var context = new LiveMarkerApplicationTestContext();
        var first = context.FirstRoom;
        var second = context.SecondRoom;
        await context.OpenDirtyAsync(first, "第一个房间打点");
        await context.OpenDirtyAsync(second, "第二个房间打点");
        var confirmed = new List<object>();
        context.Application.ConfirmLiveMarkerLeaveAsync = marker =>
        {
            confirmed.Add(marker);
            return Task.FromResult(ReferenceEquals(marker, first.LiveMarker)
                ? LiveMarkerLeaveDecision.Discard : LiveMarkerLeaveDecision.Cancel);
        };

        await context.Application.DeleteWorkspaceAsync(context.Workspace);

        Assert.Equal([first.LiveMarker, second.LiveMarker], confirmed);
        Assert.Equal(2, context.Workspace.Rooms.Count);
        Assert.True(first.LiveMarker.CanEdit);
        Assert.True(second.LiveMarker.CanEdit);
        Assert.True(first.LiveMarker.IsDirty);
        Assert.True(second.LiveMarker.IsDirty);
        Assert.Equal("第一个房间打点", Assert.Single(first.LiveMarker.Entries).Message);
        first.LiveMarker.DraftText = "取消之后继续记录";
        first.LiveMarker.AddMarkerCommand.Execute(null);
        Assert.Equal(2, first.LiveMarker.Entries.Count);
        Assert.Empty(context.Files.Saves);
    }

    /// <summary>选择保存时先保存记录和输入草稿，保存成功后才移除并释放房间。</summary>
    [Fact]
    public async Task SaveBeforeRoomDeletionPersistsLatestEntriesAndDraft()
    {
        await using var context = new LiveMarkerApplicationTestContext();
        var workspace = context.Workspace;
        var room = context.FirstRoom;
        var path = await context.OpenDirtyAsync(room, "已提交打点");
        room.LiveMarker.DraftText = "删除前尚未提交的打点";
        var wasAliveDuringSave = false;
        context.Files.BeforeSave = (_, _, _) =>
        {
            wasAliveDuringSave = workspace.Rooms.Contains(room) && room.LiveMarker.CanEdit;
            return Task.CompletedTask;
        };
        context.Application.ConfirmLiveMarkerLeaveAsync = _ => Task.FromResult(LiveMarkerLeaveDecision.Save);

        await context.Application.RemoveRoomAsync(room);

        Assert.True(wasAliveDuringSave);
        Assert.DoesNotContain(room, workspace.Rooms);
        Assert.DoesNotContain(room.State, workspace.State.LiveRooms);
        Assert.False(room.LiveMarker.CanEdit);
        var save = Assert.Single(context.Files.Saves);
        Assert.Equal(path, save.Path);
        Assert.Equal(new[] { "已提交打点", "删除前尚未提交的打点" }, save.Snapshot.Marker.Select(entry => entry.Message));
        Assert.Single(context.StateService.Load().Workspaces.Single(item => item.Id == workspace.Id).LiveRooms);
    }

    /// <summary>放弃后删除和应用异步释放都不能在后台偷偷保存已放弃的修改。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardBeforeDeletionDoesNotSaveDuringDisposal(bool deleteWorkspace)
    {
        await using var context = new LiveMarkerApplicationTestContext();
        var workspace = context.Workspace;
        var room = context.FirstRoom;
        var path = await context.OpenDirtyAsync(room, "用户决定放弃的修改");
        room.LiveMarker.DraftText = "用户决定放弃的草稿";
        context.Application.ConfirmLiveMarkerLeaveAsync = _ => Task.FromResult(LiveMarkerLeaveDecision.Discard);

        if (deleteWorkspace)
        {
            await context.Application.DeleteWorkspaceAsync(workspace);
        }
        else
        {
            await context.Application.RemoveRoomAsync(room);
        }
        await context.Application.DisposeAsync();

        Assert.Empty(context.Files.Saves);
        Assert.Empty(context.Files.Documents[path].Marker);
        Assert.False(room.LiveMarker.CanEdit);
    }

    /// <summary>写盘失败时阻止删除，保留可以继续编辑及重试保存的文档。</summary>
    [Fact]
    public async Task SaveFailureBlocksDeletionWithoutDisposingTheRoom()
    {
        await using var context = new LiveMarkerApplicationTestContext();
        var workspace = context.Workspace;
        var room = context.FirstRoom;
        var path = await context.OpenDirtyAsync(room, "写盘失败后保留");
        context.Files.SaveException = new IOException("离线模拟写盘失败");
        context.Application.ConfirmLiveMarkerLeaveAsync = _ => Task.FromResult(LiveMarkerLeaveDecision.Save);

        await context.Application.RemoveRoomAsync(room);

        Assert.Contains(room, workspace.Rooms);
        Assert.True(room.LiveMarker.CanEdit);
        Assert.True(room.LiveMarker.IsDirty);
        Assert.Empty(context.Files.Documents[path].Marker);
        Assert.Contains("离线模拟写盘失败", room.LiveMarker.LastError);
        context.Files.SaveException = null;
        await room.LiveMarker.SaveAsync();
        Assert.Equal("写盘失败后保留", Assert.Single(context.Files.Documents[path].Marker).Message);
    }

    /// <summary>表格编辑无法提交时不弹出离开确认，也不删除房间。</summary>
    [Fact]
    public async Task InvalidPendingCellEditBlocksDeletionBeforeConfirmation()
    {
        await using var context = new LiveMarkerApplicationTestContext();
        var room = context.FirstRoom;
        await context.OpenDirtyAsync(room, "保留打点");
        var confirmations = 0;
        context.Application.CommitLiveMarkerEditing = () => false;
        context.Application.ConfirmLiveMarkerLeaveAsync = _ =>
        {
            confirmations++;
            return Task.FromResult(LiveMarkerLeaveDecision.Discard);
        };

        await context.Application.RemoveRoomAsync(room);

        Assert.Equal(0, confirmations);
        Assert.Contains(room, context.Workspace.Rooms);
        Assert.True(room.LiveMarker.CanEdit);
        Assert.True(room.LiveMarker.IsDirty);
    }
}
