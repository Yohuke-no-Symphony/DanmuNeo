using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.ViewModels;

public sealed partial class ModerationManagerViewModel
{
    /// <summary>重新确认目标房间权限后，禁言当前搜索结果中的明确用户。</summary>
    [RelayCommand(CanExecute = nameof(CanMuteUser))]
    public Task MuteUserAsync(ModerationUser? user)
    {
        if (!CanMuteUser(user) || SelectedTargetRoom is not { } room)
        {
            return Task.CompletedTask;
        }

        return RunWriteAsync(room.RoomId, user!, null);
    }

    /// <summary>重新确认记录所属房间权限后，以禁言记录 ID 解除。</summary>
    [RelayCommand(CanExecute = nameof(CanUnmuteUser))]
    public Task UnmuteUserAsync(ModerationRecord? record)
    {
        return CanUnmuteUser(record) ? RunWriteAsync(record!.RoomId, null, record) : Task.CompletedTask;
    }

    private bool CanWrite()
    {
        return isActive && CanManage && !IsWriting && !IsRefreshing;
    }

    private bool CanMuteUser(ModerationUser? user)
    {
        return CanWrite() && user is { Uid: > 0 } && UserSearchResults.Contains(user) &&
               SelectedTargetRoom is { } room && ManageableRooms.Contains(room) && RoomStillExists(room.RoomId) &&
               user.Uid != room.AccountUid && user.Uid != room.OwnerUid;
    }

    private bool CanUnmuteUser(ModerationRecord? record)
    {
        return CanWrite() && record is { RecordId: > 0, RoomId: > 0 } && Records.Contains(record) &&
               ManageableRooms.Any(room => room.RoomId == record.RoomId) && RoomStillExists(record.RoomId);
    }

    private async Task RunWriteAsync(long roomId, ModerationUser? user, ModerationRecord? record)
    {
        var account = CaptureAccount();

        if (account is null)
        {
            return;
        }

        using var request = CreateRequest(account);
        var token = request.Token;
        writeCancellation = request;
        IsWriting = true;
        SetStatus("Moderation.CheckingPermission");

        try
        {
            var permission = await moderationService.GetPermissionAsync(roomId.ToString(), account.Cookie,
                getTimeout(), token);
            token.ThrowIfCancellationRequested();

            if (!IsCurrent(account) || !CanManage || !RoomStillExists(roomId) ||
                (user is not null && (!UserSearchResults.Contains(user) || SelectedTargetRoom?.RoomId != roomId)) ||
                (record is not null && !Records.Contains(record)))
            {
                return;
            }

            if (!permission.CanModerate || permission.RoomId != roomId ||
                (user is not null && (user.Uid == permission.AccountUid || user.Uid == permission.OwnerUid)))
            {
                SetStatus("Moderation.PermissionDenied");
                return;
            }

            if (record is not null)
            {
                await moderationService.UnmuteUserAsync(roomId, record.RecordId, account.Cookie, getTimeout(), token);
            }
            else
            {
                await moderationService.MuteUserAsync(roomId, user!.Uid, account.Cookie, "", getTimeout(), token);
            }

            if (IsCurrent(account) && !token.IsCancellationRequested)
            {
                dirtyRooms.Add(roomId);
                SetStatus(record is null ? "Moderation.Muted" : "Moderation.Unmuted");
                log.Information(Localization.Get("Moderation.Source"), Localization.Format(
                    record is null ? "Moderation.MuteLog" : "Moderation.UnmuteLog",
                    roomId, record?.UserUid ?? user!.Uid));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (BilibiliAuthenticationException)
        {
            if (IsCurrent(account))
            {
                accountSessions.ReportAuthenticationFailure(account.Id, account.Cookie);
                SetStatus("Moderation.CredentialsRequired");
            }
        }
        catch (Exception error)
        {
            if (IsCurrent(account) && !token.IsCancellationRequested)
            {
                SetStatus("Moderation.WriteFailed", new Func<string>(() => LocalizedExceptionText.Get(error)));
                log.Error(Localization.Get("Moderation.Source"),
                    Localization.Get("Moderation.WriteFailedLog"), error);
            }
        }
        finally
        {
            if (ReferenceEquals(writeCancellation, request))
            {
                writeCancellation = null;
                IsWriting = false;

                if (isActive && IsCurrent(account) && CanManage)
                {
                    await (RefreshTask = RefreshPendingAsync());
                }
            }
        }
    }
}
