using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.ViewModels;

public sealed partial class ModerationManagerViewModel
{
    /// <summary>重新确认所有已添加房间的权限并读取完整名单。</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh), AllowConcurrentExecutions = true)]
    public Task RefreshAsync()
    {
        if (!isActive || isDisposed || IsWriting)
        {
            return Task.CompletedTask;
        }

        CancelScan();
        fullRefreshRequired = true;
        return RefreshTask = RefreshPendingAsync();
    }

    private bool CanRefresh()
    {
        return isActive && CanManage && !IsWriting && !IsRefreshing;
    }

    private async Task RefreshPendingAsync()
    {
        if (!isActive || IsRefreshing || IsWriting || isDisposed)
        {
            return;
        }

        var account = CaptureAccount();

        if (account is null)
        {
            SetStatus(SelectedAccount is null ? "Moderation.SelectAccount" : "Moderation.CredentialsRequired");
            return;
        }

        var loopVersion = ++refreshLoopVersion;

        while (isActive && !isDisposed && CanManage && IsCurrent(account) &&
               loopVersion == refreshLoopVersion && (fullRefreshRequired || dirtyRooms.Count > 0))
        {
            var full = fullRefreshRequired;
            var pendingRooms = dirtyRooms.ToHashSet();
            fullRefreshRequired = false;
            dirtyRooms.ExceptWith(pendingRooms);
            var completed = await ScanRoomsAsync(account, full, pendingRooms);

            if (!completed)
            {
                if (IsCurrent(account) && loopVersion == refreshLoopVersion)
                {
                    fullRefreshRequired |= full;
                    dirtyRooms.UnionWith(pendingRooms);
                }

                return;
            }
        }
    }

    private async Task<bool> ScanRoomsAsync(ModerationAccountSnapshot account, bool full, HashSet<long> pendingRooms)
    {
        using var request = CreateRequest(account);
        var token = request.Token;
        scanCancellation = request;
        activeScanContextVersion = account.Version;
        activeScanIsFull = full;
        activeScanRooms = pendingRooms;
        IsRefreshing = true;
        SetStatus("Moderation.Loading");
        var sources = GetRoomSources();
        var previousRooms = ManageableRooms.ToDictionary(room => room.RoomId);
        var previousRecords = Records.GroupBy(record => record.RoomId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var nextRooms = full ? new Dictionary<long, ModerationRoomOption>() : new(previousRooms);
        var nextRecords = full ? new Dictionary<long, List<ModerationRecord>>() : new(previousRecords);
        var nextAliases = full ? new Dictionary<string, long>() : new(roomAliases);
        var nextIssues = full ? new Dictionary<long, LocalizedMessage>() : new(roomIssues);
        var processedRooms = new HashSet<long>();

        try
        {
            foreach (var source in sources)
            {
                var previousCanonical = roomAliases.GetValueOrDefault(source.Id);
                var sourceRoomId = long.Parse(source.Id, CultureInfo.InvariantCulture);

                if (!full && !pendingRooms.Contains(previousCanonical) && !pendingRooms.Contains(sourceRoomId))
                {
                    continue;
                }

                token.ThrowIfCancellationRequested();
                var canonicalRoomId = previousCanonical;

                try
                {
                    var permission = await moderationService.GetPermissionAsync(source.Id, account.Cookie,
                        getTimeout(), token);
                    token.ThrowIfCancellationRequested();

                    if (!IsCurrent(account))
                    {
                        return false;
                    }

                    canonicalRoomId = permission.RoomId;

                    if (canonicalRoomId <= 0)
                    {
                        throw LocalizedExceptionText.Create("Services.BilibiliModeration.RoomIdentityMissing",
                            text => new InvalidOperationException(text));
                    }

                    nextAliases[source.Id] = canonicalRoomId;

                    if (!permission.CanModerate)
                    {
                        nextRooms.Remove(canonicalRoomId);
                        nextRecords.Remove(canonicalRoomId);
                        nextIssues.Remove(canonicalRoomId);
                        continue;
                    }

                    if (!processedRooms.Add(canonicalRoomId))
                    {
                        continue;
                    }

                    var option = new ModerationRoomOption
                    {
                        RoomId = canonicalRoomId,
                        RoomName = source.Name,
                        AccountUid = permission.AccountUid,
                        OwnerUid = permission.OwnerUid
                    };
                    nextRooms[canonicalRoomId] = option;
                    var records = await moderationService.GetMutedUsersAsync(canonicalRoomId, account.Cookie,
                        getTimeout(), token);
                    token.ThrowIfCancellationRequested();

                    if (!IsCurrent(account))
                    {
                        return false;
                    }

                    if (records.Any(record => record.RoomId != canonicalRoomId || record.RecordId <= 0 || record.UserUid <= 0))
                    {
                        throw LocalizedExceptionText.Create("Services.BilibiliModeration.ResponseIncomplete",
                            text => new InvalidOperationException(text));
                    }

                    foreach (var record in records)
                    {
                        record.RoomName = source.Name;
                    }

                    nextRecords[canonicalRoomId] = records.DistinctBy(record => record.RecordId).ToList();
                    nextIssues.Remove(canonicalRoomId);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (BilibiliAuthenticationException)
                {
                    if (IsCurrent(account))
                    {
                        accountSessions.ReportAuthenticationFailure(account.Id, account.Cookie);
                        SetStatus("Moderation.CredentialsRequired");
                    }

                    return false;
                }
                catch (Exception error)
                {
                    token.ThrowIfCancellationRequested();

                    if (canonicalRoomId > 0)
                    {
                        nextAliases[source.Id] = canonicalRoomId;

                        if (!nextRooms.ContainsKey(canonicalRoomId) && previousRooms.TryGetValue(canonicalRoomId, out var oldRoom))
                        {
                            nextRooms[canonicalRoomId] = oldRoom;
                        }

                        if (!nextRecords.ContainsKey(canonicalRoomId) && previousRecords.TryGetValue(canonicalRoomId, out var oldRecords))
                        {
                            nextRecords[canonicalRoomId] = oldRecords;
                        }
                    }

                    var issueId = canonicalRoomId > 0 ? canonicalRoomId : sourceRoomId;
                    nextIssues[issueId] = new("Moderation.RoomFailed", source.Name.Length > 0 ? source.Name : source.Id,
                        new Func<string>(() => LocalizedExceptionText.Get(error)));
                }
            }

            token.ThrowIfCancellationRequested();

            if (!IsCurrent(account))
            {
                return false;
            }

            ApplyRooms(nextRooms, nextRecords, nextAliases, nextIssues);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(scanCancellation, request))
            {
                scanCancellation = null;
                activeScanRooms = [];
                IsRefreshing = false;
            }
        }
    }

    private List<(string Id, string Name)> GetRoomSources()
    {
        return workspaces.SelectMany(workspace => workspace.Rooms)
            .Select(room => (Parsed: long.TryParse(room.RoomId, out var id) ? id : 0, Name: room.RoomName))
            .Where(room => room.Parsed > 0)
            .DistinctBy(room => room.Parsed)
            .Select(room => (room.Parsed.ToString(CultureInfo.InvariantCulture), room.Name)).ToList();
    }

    private void ApplyRooms(Dictionary<long, ModerationRoomOption> rooms,
        Dictionary<long, List<ModerationRecord>> records, Dictionary<string, long> aliases,
        Dictionary<long, LocalizedMessage> issues)
    {
        var selectedRoomId = SelectedTargetRoom?.RoomId;
        var referencedRooms = aliases.Values.ToHashSet();
        var orderedRooms = rooms.Values.Where(room => referencedRooms.Contains(room.RoomId)).OrderBy(room => room.RoomId)
            .Select(room => ManageableRooms.FirstOrDefault(existing => existing.RoomId == room.RoomId &&
                               existing.RoomName == room.RoomName && existing.AccountUid == room.AccountUid &&
                               existing.OwnerUid == room.OwnerUid) ?? room).ToArray();

        foreach (var oldRoom in ManageableRooms.Where(room => !orderedRooms.Contains(room)).ToArray())
        {
            ManageableRooms.Remove(oldRoom);
        }

        for (var index = 0; index < orderedRooms.Length; index++)
        {
            var room = orderedRooms[index];
            var oldIndex = ManageableRooms.IndexOf(room);

            if (oldIndex < 0)
            {
                ManageableRooms.Insert(index, room);
            }
            else if (oldIndex != index)
            {
                ManageableRooms.Move(oldIndex, index);
            }
        }

        SelectedTargetRoom = ManageableRooms.FirstOrDefault(room => room.RoomId == selectedRoomId) ??
                             ManageableRooms.FirstOrDefault();
        Records.Clear();

        foreach (var record in records.Where(pair => rooms.ContainsKey(pair.Key) && referencedRooms.Contains(pair.Key)).OrderBy(pair => pair.Key)
                     .SelectMany(pair => pair.Value))
        {
            Records.Add(record);
        }

        roomAliases.Clear();

        foreach (var alias in aliases)
        {
            roomAliases.Add(alias.Key, alias.Value);
        }

        roomIssues.Clear();

        foreach (var issue in issues)
        {
            roomIssues.Add(issue.Key, issue.Value);
        }

        RenderIssues();
        NotifyCollections();
    }

    private bool RoomStillExists(long roomId)
    {
        return GetRoomSources().Any(source => long.Parse(source.Id, CultureInfo.InvariantCulture) == roomId ||
                                             roomAliases.GetValueOrDefault(source.Id) == roomId);
    }
}
