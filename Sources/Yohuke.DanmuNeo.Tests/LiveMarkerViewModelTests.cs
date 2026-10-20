using System.Threading;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证房间记录的输入锁时、行事务、历史与异步文件生命周期。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class LiveMarkerViewModelTests
{
    /// <summary>提交采用开始输入时的两个时间，不在按下提交时重新采样。</summary>
    [Fact]
    public async Task FirstCharacterLocksBothTimesUntilSubmission()
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync();
        var expectedRealTime = context.Time.GetLocalNow();
        context.ViewModel.DraftText = "event";
        Assert.True(context.ViewModel.IsTimeLocked);
        context.Time.Advance(TimeSpan.FromSeconds(30));
        context.ViewModel.AddMarkerCommand.Execute(null);
        var entry = Assert.Single(context.ViewModel.CaptureSnapshot().Marker);

        Assert.Equal(expectedRealTime, entry.RealDateTime);
        Assert.Equal(TimeSpan.FromHours(1), entry.LiveTime);
        Assert.Equal("event", entry.Message);
        Assert.False(context.ViewModel.IsTimeLocked);
        Assert.False(context.ViewModel.HasDraft);
    }

    /// <summary>手动输入前缀仅覆盖直播时间，实际时间保留首字符采样。</summary>
    [Theory]
    [InlineData("27:02:03 event")]
    [InlineData("27：02：03 event")]
    [InlineData("27.02.03 event")]
    [InlineData("27．02．03 event")]
    public async Task ManualPrefixOverridesOnlyLiveTime(string input)
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync();
        var expectedRealTime = context.Time.GetLocalNow();
        context.ViewModel.DraftText = input;
        context.Time.Advance(TimeSpan.FromSeconds(40));
        context.ViewModel.AddMarkerCommand.Execute(null);
        var entry = Assert.Single(context.ViewModel.CaptureSnapshot().Marker);

        Assert.Equal(expectedRealTime, entry.RealDateTime);
        Assert.Equal(TimeSpan.FromHours(27) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3), entry.LiveTime);
        Assert.Equal("event", entry.Message);
    }

    /// <summary>纯时间等待空白分隔符后才移除前缀，并保留最初输入时的实际时间。</summary>
    [Fact]
    public async Task TimeOnlyPrefixRemainsLockedWhenMessageArrivesLater()
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync();
        var expectedRealTime = context.Time.GetLocalNow();
        context.ViewModel.DraftText = "29:03:04";
        Assert.Equal("29:03:04", context.ViewModel.DraftText);
        Assert.True(context.ViewModel.HasDraft);
        Assert.True(context.ViewModel.IsTimeLocked);
        context.Time.Advance(TimeSpan.FromSeconds(20));
        context.ViewModel.DraftText += " later message";
        context.ViewModel.AddMarkerCommand.Execute(null);
        var entry = Assert.Single(context.ViewModel.CaptureSnapshot().Marker);

        Assert.Equal(expectedRealTime, entry.RealDateTime);
        Assert.Equal(TimeSpan.FromHours(29) + TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(4), entry.LiveTime);
        Assert.Equal("later message", entry.Message);
    }

    /// <summary>逐字输入时间不能在秒或毫秒尚未输完时吞掉部分前缀。</summary>
    [Theory]
    [InlineData("1:23:45 内容", 1, 0)]
    [InlineData("1:23:45.123 内容", 1, 123)]
    [InlineData("27：23：45 内容", 27, 0)]
    [InlineData("27．23．45.123 内容", 27, 123)]
    public async Task CharacterByCharacterPrefixWaitsForDelimiter(string text, int hours, int milliseconds)
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync();
        var firstCharacterTime = context.Time.GetLocalNow();

        foreach (var character in text)
        {
            context.ViewModel.DraftText += character;
            context.Time.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal("内容", context.ViewModel.DraftText);
        context.Time.Advance(TimeSpan.FromSeconds(2));
        context.ViewModel.AddMarkerCommand.Execute(null);
        var entry = Assert.Single(context.ViewModel.CaptureSnapshot().Marker);

        Assert.Equal(firstCharacterTime, entry.RealDateTime);
        Assert.Equal(TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(23) + TimeSpan.FromSeconds(45) +
            TimeSpan.FromMilliseconds(milliseconds), entry.LiveTime);
        Assert.Equal("内容", entry.Message);
    }

    /// <summary>自动锁时的草稿被全部删除后，新备注从新的首字符重新捕获。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ClearingAutomaticallyLockedDraftStartsANewCapture(string emptyDraft)
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync();
        context.ViewModel.DraftText = "old note";
        context.Time.Advance(TimeSpan.FromSeconds(5));
        context.ViewModel.DraftText = emptyDraft;
        Assert.False(context.ViewModel.HasDraft);
        Assert.False(context.ViewModel.IsTimeLocked);
        context.Time.Advance(TimeSpan.FromSeconds(7));
        var newCharacterTime = context.Time.GetLocalNow();
        context.ViewModel.DraftText = "new note";
        context.Time.Advance(TimeSpan.FromSeconds(11));
        context.ViewModel.AddMarkerCommand.Execute(null);
        var entry = Assert.Single(context.ViewModel.CaptureSnapshot().Marker);

        Assert.Equal(newCharacterTime, entry.RealDateTime);
        Assert.Equal(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(12), entry.LiveTime);
        Assert.Equal("new note", entry.Message);
    }

    /// <summary>主动 Escape 预锁不因删除全部草稿而被释放。</summary>
    [Fact]
    public async Task ExplicitLockSurvivesClearingTheDraft()
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync();
        var explicitLockTime = context.Time.GetLocalNow();
        context.ViewModel.LockTimeCommand.Execute(null);
        context.Time.Advance(TimeSpan.FromSeconds(5));
        context.ViewModel.DraftText = "old note";
        context.ViewModel.DraftText = "";
        Assert.False(context.ViewModel.HasDraft);
        Assert.True(context.ViewModel.IsTimeLocked);
        context.Time.Advance(TimeSpan.FromSeconds(7));
        context.ViewModel.DraftText = "new note";
        context.ViewModel.AddMarkerCommand.Execute(null);
        var entry = Assert.Single(context.ViewModel.CaptureSnapshot().Marker);

        Assert.Equal(explicitLockTime, entry.RealDateTime);
        Assert.Equal(TimeSpan.FromHours(1), entry.LiveTime);
        Assert.Equal("new note", entry.Message);
    }

    /// <summary>已通过空白分隔符确认的手工时间前缀，跨空草稿保留手工时长及首次实际时间。</summary>
    [Fact]
    public async Task ExplicitTimePrefixSurvivesClearingTheMessage()
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync();
        var firstCharacterTime = context.Time.GetLocalNow();
        context.ViewModel.DraftText = "29:03:04.123 old note";
        Assert.Equal("old note", context.ViewModel.DraftText);
        context.Time.Advance(TimeSpan.FromSeconds(5));
        context.ViewModel.DraftText = "";
        Assert.False(context.ViewModel.HasDraft);
        Assert.True(context.ViewModel.IsTimeLocked);
        context.Time.Advance(TimeSpan.FromSeconds(7));
        context.ViewModel.DraftText = "new note";
        context.ViewModel.AddMarkerCommand.Execute(null);
        var entry = Assert.Single(context.ViewModel.CaptureSnapshot().Marker);

        Assert.Equal(firstCharacterTime, entry.RealDateTime);
        Assert.Equal(TimeSpan.FromHours(29) + TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(4) +
            TimeSpan.FromMilliseconds(123), entry.LiveTime);
        Assert.Equal("new note", entry.Message);
    }

    /// <summary>输入前主动锁时，首个字符不得覆盖该锁。</summary>
    [Fact]
    public async Task ManualLockBeforeTypingKeepsItsOriginalTime()
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync();
        context.ViewModel.LockTimeCommand.Execute(null);
        var expectedRealTime = context.Time.GetLocalNow();
        context.Time.Advance(TimeSpan.FromSeconds(15));
        context.ViewModel.DraftText = "event";
        context.ViewModel.AddMarkerCommand.Execute(null);
        var entry = Assert.Single(context.ViewModel.CaptureSnapshot().Marker);

        Assert.Equal(expectedRealTime, entry.RealDateTime);
        Assert.Equal(TimeSpan.FromHours(1), entry.LiveTime);
    }

    /// <summary>主动解锁后的提交按新的提交时刻记录。</summary>
    [Fact]
    public async Task UnlockAllowsSubmissionToCaptureFreshTimes()
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync();
        context.ViewModel.DraftText = "event";
        context.Time.Advance(TimeSpan.FromSeconds(20));
        context.ViewModel.UnlockTimeCommand.Execute(null);
        context.Time.Advance(TimeSpan.FromSeconds(10));
        context.ViewModel.AddMarkerCommand.Execute(null);
        var entry = Assert.Single(context.ViewModel.CaptureSnapshot().Marker);

        Assert.Equal(context.Time.GetLocalNow(), entry.RealDateTime);
        Assert.Equal(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(30), entry.LiveTime);
    }

    /// <summary>编辑直播时间及撤销重做均不推导实际时间，未编辑行保持实例。</summary>
    [Fact]
    public async Task RowEditUndoRedoKeepsIndependentRealTime()
    {
        using var context = new LiveMarkerSessionTestContext();
        var entry = CreateIndependentEntry();
        await context.OpenAsync(new()
        {
            StartTime = context.Clock.StartTime,
            Marker = [entry, entry with { Message = "unaffected" }]
        });
        var edited = context.ViewModel.Entries[0];
        var unaffected = context.ViewModel.Entries[1];
        edited.BeginEdit();
        edited.LiveTimeText = "35:02:03.456";
        edited.Message = "edited";
        edited.MarkerColor = LiveMarkerColor.Green;
        edited.EndEdit();
        Assert.True(context.ViewModel.IsDirty);
        Assert.Equal(entry.RealDateTime, edited.Snapshot.RealDateTime);
        Assert.Equal(TimeSpan.FromHours(35) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3) +
            TimeSpan.FromMilliseconds(456), edited.Snapshot.LiveTime);

        context.ViewModel.UndoCommand.Execute(null);
        Assert.Equal(entry, context.ViewModel.Entries[0].Snapshot);
        Assert.Same(unaffected, context.ViewModel.Entries[1]);
        Assert.False(context.ViewModel.IsDirty);
        context.ViewModel.RedoCommand.Execute(null);
        Assert.Equal("edited", context.ViewModel.Entries[0].Message);
        Assert.Equal(entry.RealDateTime, context.ViewModel.Entries[0].Snapshot.RealDateTime);
        Assert.Equal(LiveMarkerColor.Green, context.ViewModel.Entries[0].MarkerColor);
        Assert.True(context.ViewModel.IsDirty);
    }

    /// <summary>取消表格编辑恢复草稿字段，不改变提交快照或历史。</summary>
    [Fact]
    public async Task CancelRowEditKeepsCommittedSnapshotClean()
    {
        using var context = new LiveMarkerSessionTestContext();
        var entry = CreateIndependentEntry();
        await context.OpenAsync(new() { StartTime = context.Clock.StartTime, Marker = [entry] });
        var row = Assert.Single(context.ViewModel.Entries);
        row.BeginEdit();
        row.LiveTimeText = "40:00:00";
        row.Message = "canceled";
        row.MarkerColor = LiveMarkerColor.Blue;
        Assert.Equal(entry, row.Snapshot);
        row.CancelEdit();

        Assert.Equal(entry.Message, row.Message);
        Assert.Equal(entry.MarkerColor, row.MarkerColor);
        Assert.Equal(entry, row.Snapshot);
        Assert.False(context.ViewModel.IsDirty);
        Assert.False(context.ViewModel.UndoCommand.CanExecute(null));
    }

    /// <summary>无变化提交和仅修改备注都保留旧文件中毫秒以下的直播时长精度。</summary>
    [Fact]
    public async Task UnchangedTimeTextDoesNotTruncateSubMillisecondTicks()
    {
        using var context = new LiveMarkerSessionTestContext();
        var entry = CreateIndependentEntry() with { LiveTime = TimeSpan.FromTicks(123456789) };
        await context.OpenAsync(new() { StartTime = context.Clock.StartTime, Marker = [entry] });
        var row = Assert.Single(context.ViewModel.Entries);
        row.BeginEdit();
        row.EndEdit();
        Assert.Equal(entry, row.Snapshot);
        Assert.False(context.ViewModel.IsDirty);
        row.BeginEdit();
        row.Message = "message only";
        row.EndEdit();
        Assert.Equal(entry.LiveTime, row.Snapshot.LiveTime);
        Assert.Equal(entry.RealDateTime, row.Snapshot.RealDateTime);
    }

    /// <summary>非法表格时间显示错误，不能提交到文档；取消后清除错误。</summary>
    [Theory]
    [InlineData("-01:00:00")]
    [InlineData("25:61:00")]
    [InlineData("invalid")]
    public async Task InvalidRowTimeCannotChangeCommittedDocument(string text)
    {
        using var context = new LiveMarkerSessionTestContext();
        var entry = CreateIndependentEntry();
        await context.OpenAsync(new() { StartTime = context.Clock.StartTime, Marker = [entry] });
        var row = Assert.Single(context.ViewModel.Entries);
        row.BeginEdit();
        row.LiveTimeText = text;
        Assert.True(row.HasErrors);
        Assert.NotEmpty(row.GetErrors(nameof(row.LiveTimeText)).Cast<string>());
        row.EndEdit();
        Assert.Equal(entry, row.Snapshot);
        Assert.False(context.ViewModel.IsDirty);
        row.CancelEdit();
        Assert.False(row.HasErrors);
    }

    /// <summary>保存只读取行的已提交快照，不会把尚可取消的表格内容写入磁盘。</summary>
    [Fact]
    public async Task SavingDuringUncommittedRowEditUsesCommittedValues()
    {
        using var context = new LiveMarkerSessionTestContext();
        var entry = CreateIndependentEntry();
        var path = await context.OpenAsync(new() { StartTime = context.Clock.StartTime, Marker = [entry] });
        var row = Assert.Single(context.ViewModel.Entries);
        row.BeginEdit();
        row.Message = "temporary";
        row.LiveTimeText = "50:00:00";
        await context.ViewModel.SaveAsync();
        Assert.Equal(entry, Assert.Single(context.Files.Documents[path].Marker));
        row.CancelEdit();
        Assert.Equal(entry, row.Snapshot);
        Assert.False(context.ViewModel.IsDirty);
    }

    /// <summary>撤销回保存节点变干净；同位置的新分支仍是不同内容节点。</summary>
    [Fact]
    public async Task UndoRedoSavedNodeAndNewBranchKeepDirtyStateCorrect()
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync();
        Add(context, "saved");
        await context.ViewModel.SaveAsync();
        var first = Assert.Single(context.ViewModel.Entries);
        Add(context, "second");
        Assert.True(context.ViewModel.IsDirty);
        context.ViewModel.UndoCommand.Execute(null);
        Assert.False(context.ViewModel.IsDirty);
        Assert.Same(first, Assert.Single(context.ViewModel.Entries));
        context.ViewModel.RedoCommand.Execute(null);
        Assert.True(context.ViewModel.IsDirty);
        context.ViewModel.UndoCommand.Execute(null);
        context.ViewModel.UndoCommand.Execute(null);
        Add(context, "new branch");
        Assert.True(context.ViewModel.IsDirty);
        Assert.False(context.ViewModel.RedoCommand.CanExecute(null));
    }

    /// <summary>删除只影响选中行，撤销保持记录的两个独立时间。</summary>
    [Fact]
    public async Task DeleteUndoRestoresIndependentSnapshot()
    {
        using var context = new LiveMarkerSessionTestContext();
        var entry = CreateIndependentEntry();
        await context.OpenAsync(new() { StartTime = context.Clock.StartTime, Marker = [entry] });
        context.ViewModel.SelectedEntry = context.ViewModel.Entries[0];
        context.ViewModel.DeleteSelectedCommand.Execute(null);
        Assert.Empty(context.ViewModel.Entries);
        Assert.True(context.ViewModel.IsDirty);
        context.ViewModel.UndoCommand.Execute(null);
        Assert.Equal(entry, Assert.Single(context.ViewModel.Entries).Snapshot);
        Assert.False(context.ViewModel.IsDirty);
    }

    /// <summary>旧快照保存完成后，期间提交的新编辑仍保持脏状态。</summary>
    [Fact]
    public async Task EditingDuringSaveDoesNotBecomeFalselyClean()
    {
        using var context = new LiveMarkerSessionTestContext();
        var path = await context.OpenAsync();
        Add(context, "before save");
        var started = NewCompletion();
        var release = NewCompletion();
        context.Files.BeforeSave = async (_, _, cancellationToken) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        var saving = context.ViewModel.SaveAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditMessage(context, "after snapshot");
        release.TrySetResult();
        await saving;

        Assert.Equal("before save", context.Files.Documents[path].Marker[0].Message);
        Assert.Equal("after snapshot", context.ViewModel.Entries[0].Snapshot.Message);
        Assert.True(context.ViewModel.IsDirty);
        context.Files.BeforeSave = null;
        await context.ViewModel.SaveAsync();
        Assert.Equal("after snapshot", context.Files.Documents[path].Marker[0].Message);
        Assert.False(context.ViewModel.IsDirty);
    }

    /// <summary>并发发起的两次保存按顺序写入，后一快照包含最新提交。</summary>
    [Fact]
    public async Task SaveRequestsAreSerializedAndLastSnapshotWins()
    {
        using var context = new LiveMarkerSessionTestContext();
        var path = await context.OpenAsync();
        Add(context, "first");
        var started = NewCompletion();
        var release = NewCompletion();
        context.Files.BeforeSave = async (_, _, cancellationToken) =>
        {
            if (context.Files.Saves.Count == 1)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
        };
        var firstSave = context.ViewModel.SaveAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditMessage(context, "second");
        var secondSave = context.ViewModel.SaveAsync();
        Assert.Single(context.Files.Saves);
        Assert.False(secondSave.IsCompleted);
        release.TrySetResult();
        await Task.WhenAll(firstSave, secondSave);

        Assert.Equal(2, context.Files.Saves.Count);
        Assert.Equal("first", context.Files.Saves[0].Snapshot.Marker[0].Message);
        Assert.Equal("second", context.Files.Documents[path].Marker[0].Message);
        Assert.False(context.ViewModel.IsDirty);
    }

    /// <summary>显式保存失败可观察且保留文档，之后成功重试不会被失败任务阻塞。</summary>
    [Fact]
    public async Task FailedSavePreservesDocumentAndAllowsRetry()
    {
        using var context = new LiveMarkerSessionTestContext();
        var path = await context.OpenAsync();
        Add(context, "keep");
        context.Files.SaveException = new IOException("write failure");
        await Assert.ThrowsAsync<IOException>(() => context.ViewModel.SaveAsync());
        Assert.True(context.ViewModel.IsDirty);
        Assert.False(context.ViewModel.IsSaving);
        Assert.Equal(path, context.ViewModel.FilePath);
        Assert.Equal("keep", Assert.Single(context.ViewModel.Entries).Message);
        context.Files.SaveException = null;
        await context.ViewModel.SaveAsync();
        Assert.False(context.ViewModel.IsDirty);
    }

    /// <summary>新建取时失败或候选写盘失败时，不替换当前文档、路径和草稿。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedNewKeepsCurrentDocumentAndDraft(bool failClock)
    {
        using var context = new LiveMarkerSessionTestContext();
        var oldPath = await context.OpenAsync();
        Add(context, "keep");
        context.ViewModel.DraftText = "draft";
        Assert.True(await context.ViewModel.PrepareToLeaveAsync(_ => Task.FromResult(LiveMarkerLeaveDecision.Discard)));
        var oldRow = Assert.Single(context.ViewModel.Entries);
        var candidate = LiveMarkerSessionTestContext.NewPath();

        if (failClock)
        {
            context.Clock.Exception = new IOException("clock failure");
        }
        else
        {
            context.Files.SaveException = new IOException("write failure");
        }

        await Assert.ThrowsAsync<IOException>(() => context.ViewModel.NewAsync(candidate));
        Assert.Equal(oldPath, context.ViewModel.FilePath);
        Assert.Equal(oldPath, context.State.LiveMarkerFilePath);
        Assert.Same(oldRow, Assert.Single(context.ViewModel.Entries));
        Assert.Equal("draft", context.ViewModel.DraftText);
        Assert.True(context.ViewModel.IsDirty);
        Assert.False(context.ViewModel.IsBusy);
        using var candidateLease = context.Files.Acquire(candidate, new object());
    }

    /// <summary>候选读取失败保留旧文档与关联，并释放候选租约。</summary>
    [Fact]
    public async Task FailedOpenKeepsCurrentDocumentAndReleasesCandidateLease()
    {
        using var context = new LiveMarkerSessionTestContext();
        var oldPath = await context.OpenAsync();
        Add(context, "keep");
        context.ViewModel.DraftText = "draft";
        var oldRow = Assert.Single(context.ViewModel.Entries);
        var candidate = LiveMarkerSessionTestContext.NewPath();
        context.Files.LoadException = new InvalidDataException("invalid YAML");

        await Assert.ThrowsAsync<InvalidDataException>(() => context.ViewModel.OpenAsync(candidate));
        Assert.Equal(oldPath, context.ViewModel.FilePath);
        Assert.Same(oldRow, Assert.Single(context.ViewModel.Entries));
        Assert.Equal("draft", context.ViewModel.DraftText);
        Assert.True(context.ViewModel.IsDirty);
        using var candidateLease = context.Files.Acquire(candidate, new object());
    }

    /// <summary>同 B 站房间的两个工作区标签持有互相独立的文件、草稿和时钟。</summary>
    [Fact]
    public async Task RoomTabsKeepIndependentFilesAndDrafts()
    {
        var files = new LiveMarkerSessionTestFiles();
        using var first = new LiveMarkerSessionTestContext(files: files);
        using var second = new LiveMarkerSessionTestContext(files: files);
        var firstPath = await first.OpenAsync();
        var secondPath = await second.OpenAsync(new() { StartTime = second.Time.GetLocalNow() - TimeSpan.FromHours(2) });
        first.ViewModel.DraftText = "first draft";
        second.ViewModel.DraftText = "second draft";
        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal("first draft", first.ViewModel.DraftText);
        Assert.Equal("second draft", second.ViewModel.DraftText);
        first.ViewModel.AddMarkerCommand.Execute(null);
        Assert.Equal(TimeSpan.FromHours(1), Assert.Single(first.ViewModel.Entries).Snapshot.LiveTime);
        second.ViewModel.AddMarkerCommand.Execute(null);
        Assert.Equal(TimeSpan.FromHours(2), Assert.Single(second.ViewModel.Entries).Snapshot.LiveTime);
    }

    /// <summary>另一标签不能同时打开同一文件；原所有者关闭后才可接管。</summary>
    [Fact]
    public async Task SharedFileLeasePreventsConcurrentRoomOwners()
    {
        var files = new LiveMarkerSessionTestFiles();
        using var first = new LiveMarkerSessionTestContext(files: files);
        using var second = new LiveMarkerSessionTestContext(files: files);
        var path = await first.OpenAsync();
        await Assert.ThrowsAsync<IOException>(() => second.ViewModel.OpenAsync(path));
        Assert.True(first.ViewModel.HasDocument);
        Assert.False(second.ViewModel.HasDocument);
        Assert.Equal(path, first.ViewModel.FilePath);
        await first.ViewModel.CloseAsync();
        await second.ViewModel.OpenAsync(path);
        Assert.Equal(path, second.ViewModel.FilePath);
    }

    /// <summary>重启恢复仅读取文件起点与原条目，不访问 B 站取时。</summary>
    [Fact]
    public async Task RestoreUsesFileOriginOfflineAndRunsOnce()
    {
        var path = LiveMarkerSessionTestContext.NewPath();
        using var context = new LiveMarkerSessionTestContext(state: new() { RoomId = "100", LiveMarkerFilePath = path });
        var entry = CreateIndependentEntry();
        var origin = context.Time.GetLocalNow() - TimeSpan.FromHours(30);
        context.Files.Documents[path] = new() { StartTime = origin, Marker = [entry] };
        await context.ViewModel.RestoreAsync();
        await context.ViewModel.RestoreAsync();

        Assert.Empty(context.Clock.Requests);
        Assert.Single(context.Files.Loads);
        Assert.Equal(origin, context.ViewModel.CaptureSnapshot().StartTime);
        Assert.Equal(entry, Assert.Single(context.ViewModel.Entries).Snapshot);
        context.ViewModel.DraftText = "fresh";
        context.ViewModel.AddMarkerCommand.Execute(null);
        Assert.Equal(TimeSpan.FromHours(30), context.ViewModel.Entries[1].Snapshot.LiveTime);
    }

    /// <summary>离开时取消或放弃只表达许可，均不提前清空内存文档或草稿。</summary>
    [Theory]
    [InlineData(LiveMarkerLeaveDecision.Cancel, false)]
    [InlineData(LiveMarkerLeaveDecision.Discard, true)]
    public async Task CancelOrDiscardDoesNotMutateDocumentDuringPreflight(LiveMarkerLeaveDecision decision, bool expected)
    {
        using var context = new LiveMarkerSessionTestContext();
        var path = await context.OpenAsync();
        Add(context, "unsaved");
        context.ViewModel.DraftText = "draft";
        var row = Assert.Single(context.ViewModel.Entries);
        Assert.Equal(expected, await context.ViewModel.PrepareToLeaveAsync(_ => Task.FromResult(decision)));

        Assert.True(context.ViewModel.HasDocument);
        Assert.True(context.ViewModel.IsDirty);
        Assert.Equal(path, context.ViewModel.FilePath);
        Assert.Same(row, Assert.Single(context.ViewModel.Entries));
        Assert.Equal("draft", context.ViewModel.DraftText);
        Assert.Empty(context.Files.Saves);
    }

    /// <summary>离开选择保存时提交锁时草稿并保存后才返回许可。</summary>
    [Fact]
    public async Task LeaveSaveSubmitsLockedDraftThenPersistsIt()
    {
        using var context = new LiveMarkerSessionTestContext();
        var path = await context.OpenAsync();
        var expectedRealTime = context.Time.GetLocalNow();
        context.ViewModel.DraftText = "draft";
        context.Time.Advance(TimeSpan.FromSeconds(25));
        Assert.True(await context.ViewModel.PrepareToLeaveAsync(_ => Task.FromResult(LiveMarkerLeaveDecision.Save)));
        var entry = Assert.Single(context.Files.Documents[path].Marker);

        Assert.Equal(expectedRealTime, entry.RealDateTime);
        Assert.Equal(TimeSpan.FromHours(1), entry.LiveTime);
        Assert.Equal("draft", entry.Message);
        Assert.False(context.ViewModel.HasDraft);
        Assert.False(context.ViewModel.IsDirty);
    }

    /// <summary>离开保存失败返回 false，草稿已提交的内容仍留在文档中供重试。</summary>
    [Fact]
    public async Task LeaveSaveFailureKeepsAllContentAndRejectsLeaving()
    {
        using var context = new LiveMarkerSessionTestContext();
        var path = await context.OpenAsync();
        context.ViewModel.DraftText = "draft";
        context.Files.SaveException = new IOException("write failure");
        Assert.False(await context.ViewModel.PrepareToLeaveAsync(_ => Task.FromResult(LiveMarkerLeaveDecision.Save)));

        Assert.Equal(path, context.ViewModel.FilePath);
        Assert.Equal("draft", Assert.Single(context.ViewModel.Entries).Snapshot.Message);
        Assert.True(context.ViewModel.IsDirty);
        Assert.True(context.ViewModel.HasDocument);
    }

    /// <summary>时间起点超前导致草稿不能提交时，保存离开失败但草稿仍保留。</summary>
    [Fact]
    public async Task InvalidDraftTimeRejectsLeaveWithoutLosingDraft()
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync(new() { StartTime = context.Time.GetLocalNow() + TimeSpan.FromHours(1) });
        context.ViewModel.DraftText = "future origin";
        Assert.False(context.ViewModel.CanAddMarker);
        Assert.False(await context.ViewModel.PrepareToLeaveAsync(_ => Task.FromResult(LiveMarkerLeaveDecision.Save)));
        Assert.Equal("future origin", context.ViewModel.DraftText);
        Assert.Empty(context.ViewModel.Entries);
    }

    /// <summary>另存为写入期间产生的新提交不应被旧候选快照标为已保存。</summary>
    [Fact]
    public async Task SaveAsKeepsConcurrentCommittedEditDirty()
    {
        using var context = new LiveMarkerSessionTestContext();
        await context.OpenAsync();
        Add(context, "before snapshot");
        var started = NewCompletion();
        var release = NewCompletion();
        var candidate = LiveMarkerSessionTestContext.NewPath();
        context.Files.BeforeSave = async (_, _, cancellationToken) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        var saving = context.ViewModel.SaveAsAsync(candidate);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        EditMessage(context, "after snapshot");
        release.TrySetResult();
        await saving;

        Assert.Equal(candidate, context.ViewModel.FilePath);
        Assert.Equal("before snapshot", context.Files.Documents[candidate].Marker[0].Message);
        Assert.Equal("after snapshot", context.ViewModel.Entries[0].Snapshot.Message);
        Assert.True(context.ViewModel.IsDirty);
    }

    /// <summary>两种导出均不可覆盖当前或其他标签持有的 YAML，包括指向它的符号链接。</summary>
    [Theory]
    [InlineData(false, "current")]
    [InlineData(true, "current")]
    [InlineData(false, "other")]
    [InlineData(true, "other")]
    [InlineData(false, "symbolic-link")]
    [InlineData(true, "symbolic-link")]
    public async Task ExportCannotOverwriteAnOpenDocument(bool excel, string targetKind)
    {
        if (targetKind == "symbolic-link" && OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "danmu-marker-export-guard", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var files = new LiveMarkerSessionTestFiles();
            using var first = new LiveMarkerSessionTestContext(files: files);
            using var second = new LiveMarkerSessionTestContext(files: files);
            var yamlPath = Path.Combine(directory, "protected.yaml");
            const string ORIGINAL_YAML = "StartTime: 2026-10-20T11:00:00+00:00\nMarker: []\n";
            await File.WriteAllTextAsync(yamlPath, ORIGINAL_YAML);
            files.Documents[yamlPath] = new() { StartTime = first.Clock.StartTime };

            if (targetKind == "other")
            {
                await first.OpenAsync();
                await second.ViewModel.OpenAsync(yamlPath);
            }
            else
            {
                await first.ViewModel.OpenAsync(yamlPath);
            }

            var outputPath = yamlPath;

            if (targetKind == "symbolic-link")
            {
                outputPath = Path.Combine(directory, excel ? "export.xlsx" : "export.txt");
                File.CreateSymbolicLink(outputPath, yamlPath);
            }

            Add(first, "unsaved record");
            first.ViewModel.DraftText = "pending draft";
            var before = first.ViewModel.CaptureSnapshot();
            var originalPath = first.ViewModel.FilePath;
            var originalDirty = first.ViewModel.IsDirty;

            await Assert.ThrowsAsync<IOException>(() => excel
                ? first.ViewModel.ExportExcelAsync(outputPath)
                : first.ViewModel.ExportTextAsync(outputPath));

            var after = first.ViewModel.CaptureSnapshot();
            Assert.Equal(before.StartTime, after.StartTime);
            Assert.Equal(before.Marker.ToArray(), after.Marker.ToArray());
            Assert.Equal(originalPath, first.ViewModel.FilePath);
            Assert.Equal(originalPath, first.State.LiveMarkerFilePath);
            Assert.Equal(originalDirty, first.ViewModel.IsDirty);
            Assert.Equal("pending draft", first.ViewModel.DraftText);
            Assert.Equal(ORIGINAL_YAML, await File.ReadAllTextAsync(yamlPath));
            Assert.Empty(files.Saves);

            if (targetKind == "other")
            {
                Assert.Equal(yamlPath, second.ViewModel.FilePath);
                Assert.Empty(second.ViewModel.CaptureSnapshot().Marker);
                Assert.False(second.ViewModel.IsDirty);
            }

            if (targetKind == "symbolic-link")
            {
                Assert.NotNull(new FileInfo(outputPath).LinkTarget);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>写盘忽略取消并迟到完成时，销毁或房间操作取消都不能安装另存为结果。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidatedSaveAsCannotChangePersistedFilePath(bool dispose)
    {
        var files = new LiveMarkerSessionTestFiles();
        using var context = new LiveMarkerSessionTestContext(files: files);
        using var services = new Yohuke.DanmuNeo.Services.LiveMarkerServices(
            new LiveMarkerUncancelableTestFiles(files), context.Clock, timeProvider: context.Time);
        var state = new LiveRoomTabState { RoomId = "100" };
        using var viewModel = new Yohuke.DanmuNeo.ViewModels.Items.LiveMarkerViewModel(state);
        viewModel.Configure(services, () => state.RoomId, () => null, () => context.Settings,
            () => Task.CompletedTask, null);
        var originalPath = LiveMarkerSessionTestContext.NewPath();
        var originalDocument = new LiveMarkerDocument
        {
            StartTime = context.Clock.StartTime,
            Marker = [CreateIndependentEntry()]
        };
        files.Documents[originalPath] = originalDocument;
        await viewModel.OpenAsync(originalPath);
        viewModel.DraftText = "unsaved record";
        viewModel.AddMarkerCommand.Execute(null);
        var started = NewCompletion();
        var release = NewCompletion();
        files.BeforeSave = async (_, _, _) =>
        {
            started.TrySetResult();
            await release.Task;
        };
        var candidatePath = LiveMarkerSessionTestContext.NewPath();
        var saving = viewModel.SaveAsAsync(candidatePath);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        if (dispose)
        {
            viewModel.Dispose();
        }
        else
        {
            viewModel.CancelPendingOperation();
        }

        release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saving);
        Assert.Equal(originalPath, state.LiveMarkerFilePath);
        Assert.Equal(originalPath, viewModel.FilePath);
        Assert.Equal(originalDocument.StartTime, files.Documents[originalPath].StartTime);
        Assert.Equal(originalDocument.Marker.ToArray(), files.Documents[originalPath].Marker.ToArray());
        using var candidateLease = files.Acquire(candidatePath, new object());

        if (!dispose)
        {
            Assert.True(viewModel.IsDirty);
            Assert.Equal(2, viewModel.Entries.Count);
            Assert.Equal("unsaved record", viewModel.Entries[1].Snapshot.Message);
            Assert.Throws<IOException>(() => files.Acquire(originalPath, new object()));
        }
    }

    /// <summary>房号变化使迟到的新建取时失效，不得替换原文档或保留候选租约。</summary>
    [Fact]
    public async Task CanceledRoomOperationCannotInstallLateClockResult()
    {
        using var context = new LiveMarkerSessionTestContext();
        var originalPath = await context.OpenAsync();
        var clockResponse = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Clock.Response = _ => clockResponse.Task;
        var candidate = LiveMarkerSessionTestContext.NewPath();
        var creating = context.ViewModel.NewAsync(candidate);
        context.State.RoomId = "200";
        context.ViewModel.CancelPendingOperation();
        clockResponse.TrySetResult(context.Clock.StartTime);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creating);

        Assert.Equal(originalPath, context.ViewModel.FilePath);
        Assert.True(context.ViewModel.HasDocument);
        Assert.Empty(context.Files.Saves);
        using var lease = context.Files.Acquire(candidate, new object());
    }

    private static TaskCompletionSource NewCompletion()
    {
        return new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static LiveMarkerEntry CreateIndependentEntry()
    {
        return new()
        {
            RealDateTime = new(2026, 10, 20, 15, 42, 30, TimeSpan.FromHours(3)),
            LiveTime = TimeSpan.FromMinutes(4),
            Message = "original",
            MarkerColor = LiveMarkerColor.Magenta
        };
    }

    private static void Add(LiveMarkerSessionTestContext context, string message)
    {
        context.ViewModel.DraftText = message;
        context.ViewModel.AddMarkerCommand.Execute(null);
    }

    private static void EditMessage(LiveMarkerSessionTestContext context, string message)
    {
        var row = context.ViewModel.Entries[0];
        row.BeginEdit();
        row.Message = message;
        row.EndEdit();
    }
}
