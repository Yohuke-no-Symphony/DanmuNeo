using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class LiveRoomTabViewModel
{
    private IBilibiliModerationService moderationService = new BilibiliModerationService();
    private CancellationTokenSource? moderationTokenSource;
    private RoomModerationPermission? moderationPermission;
    private string moderationRoomId = "";
    private string moderationAccountId = "";
    private string moderationCookie = "";

    [ObservableProperty] private string moderationStatus = "";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(MuteDanmuCommand))]
    private bool isMutingDanmu;

    /// <summary>
    /// 使当前禁言权限与尚未完成的操作失效。
    /// </summary>
    public void InvalidateModerationPermission()
    {
        var tokenSource = moderationTokenSource;
        moderationTokenSource = null;
        tokenSource?.Cancel();
        moderationPermission = null;
        moderationCookie = "";
        moderationAccountId = "";
        moderationRoomId = "";
        IsMutingDanmu = false;
        SetModerationStatus("");
        MuteDanmuCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 查询当前账号是否能够禁言指定弹幕的发送者。
    /// </summary>
    public async Task<bool> RefreshModerationPermissionAsync(DanmuFeedItem item,
        CancellationToken cancellationToken = default)
    {
        if (IsMutingDanmu || !IsModerationTargetEligible(item))
        {
            return false;
        }

        InvalidateModerationPermission();
        var account = resolveAccount?.Invoke(this);

        if (account is null || string.IsNullOrWhiteSpace(account.Cookie))
        {
            SetModerationStatus("Room.ModerationAccountRequired");
            log?.Warning(CreateLogSource(Localization.Get("Room.Log.ModerationPermission")), Localization.Get("Room.Log.ModerationAccountMissing"));
            return false;
        }

        var roomId = RoomId;
        var source = CreateLogSource(Localization.Get("Room.Log.ModerationPermission"), roomId);
        var accountId = account.Id;
        var cookie = account.Cookie;
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        moderationTokenSource = tokenSource;

        try
        {
            var permission = await moderationService.GetPermissionAsync(
                roomId, cookie, GetModerationTimeout(), tokenSource.Token);

            if (!IsCurrentModerationOperation(tokenSource, roomId, accountId, cookie))
            {
                return false;
            }

            moderationPermission = permission;
            moderationRoomId = roomId;
            moderationAccountId = accountId;
            moderationCookie = cookie;
            SetModerationStatus(permission.CanModerate ? "" : "Room.ModerationNotAllowed");
            MuteDanmuCommand.NotifyCanExecuteChanged();
            return CanMuteDanmu(item);
        }
        catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            if (IsCurrentModerationOperation(tokenSource, roomId, accountId, cookie))
            {
                log?.Error(source, Localization.Get("Room.Log.ModerationPermissionFailed"), exception);
                SetModerationStatus("Room.ModerationQueryFailed", new Func<string>(() => LocalizedExceptionText.Get(exception)));
            }

            return false;
        }
        finally
        {
            if (ReferenceEquals(moderationTokenSource, tokenSource))
            {
                moderationTokenSource = null;
            }

            tokenSource.Dispose();
        }
    }

    /// <summary>
    /// 再次确认权限后禁言弹幕发送者。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMuteDanmu), AllowConcurrentExecutions = true)]
    public async Task MuteDanmuAsync(DanmuFeedItem? item)
    {
        if (!CanMuteDanmu(item) || item is null)
        {
            return;
        }

        var roomId = moderationRoomId;
        var source = CreateLogSource(Localization.Get("Room.Log.Mute"), roomId);
        var accountId = moderationAccountId;
        var cookie = moderationCookie;
        var userUid = long.Parse(item.UserUid);
        var message = item.Content;
        InvalidateModerationPermission();
        var tokenSource = new CancellationTokenSource();
        moderationTokenSource = tokenSource;
        IsMutingDanmu = true;
        SetModerationStatus("Room.ModerationMuting", item.UserName);

        try
        {
            var permission = await moderationService.GetPermissionAsync(
                roomId, cookie, GetModerationTimeout(), tokenSource.Token);

            if (!IsCurrentModerationOperation(tokenSource, roomId, accountId, cookie))
            {
                return;
            }

            if (!permission.CanModerate || !IsModerationTargetEligible(item) ||
                userUid == permission.AccountUid || userUid == permission.OwnerUid)
            {
                SetModerationStatus("Room.ModerationIdentityChanged");
                log?.Warning(source, Localization.Format("Room.Log.MuteIdentityChanged", userUid));
                return;
            }

            await moderationService.MuteUserAsync(permission.RoomId, userUid, cookie, message, GetModerationTimeout(),
                tokenSource.Token);

            if (IsCurrentModerationOperation(tokenSource, roomId, accountId, cookie))
            {
                log?.Information(source, Localization.Format("Room.Log.MutedUser", userUid));
                SetModerationStatus("Room.ModerationMuted", item.UserName, userUid);
            }
        }
        catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentModerationOperation(tokenSource, roomId, accountId, cookie))
            {
                log?.Error(source, Localization.Format("Room.Log.MuteFailed", userUid), exception);
                SetModerationStatus("Room.ModerationFailed", new Func<string>(() => LocalizedExceptionText.Get(exception)));
            }
        }
        finally
        {
            if (ReferenceEquals(moderationTokenSource, tokenSource))
            {
                moderationTokenSource = null;
                IsMutingDanmu = false;
            }

            tokenSource.Dispose();
        }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (!value)
        {
            InvalidateModerationPermission();
        }
    }

    private bool CanMuteDanmu(DanmuFeedItem? item)
    {
        return !IsMutingDanmu && item is not null && IsModerationTargetEligible(item) &&
               moderationPermission is { CanModerate: true } permission &&
               IsModerationAccountCurrent(moderationRoomId, moderationAccountId, moderationCookie) &&
               long.TryParse(item.UserUid, out var uid) && uid != permission.AccountUid && uid != permission.OwnerUid;
    }

    private bool IsModerationTargetEligible(DanmuFeedItem item)
    {
        return DanmuItems.Contains(item) && long.TryParse(item.RoomId, out var sourceRoomId) &&
               long.TryParse(RoomId, out var currentRoomId) && sourceRoomId == currentRoomId &&
               !item.IsLocalRecord && !item.IsRoomAdmin &&
               long.TryParse(item.UserUid, out var uid) && uid > 0;
    }

    private bool IsCurrentModerationOperation(CancellationTokenSource tokenSource, string roomId, string accountId,
        string cookie)
    {
        return !tokenSource.IsCancellationRequested && ReferenceEquals(moderationTokenSource, tokenSource) &&
               IsModerationAccountCurrent(roomId, accountId, cookie);
    }

    private bool IsModerationAccountCurrent(string roomId, string accountId, string cookie)
    {
        var account = resolveAccount?.Invoke(this);
        return RoomId == roomId && account is not null && account.Id == accountId && account.Cookie == cookie;
    }

    private TimeSpan GetModerationTimeout()
    {
        return TimeSpan.FromSeconds(getSettings?.Invoke().TimeoutSeconds ?? 3.05);
    }
}
