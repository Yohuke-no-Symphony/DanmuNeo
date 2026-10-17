using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        ModerationStatus = "";
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
            ModerationStatus = "缺少登录账号";
            log?.Warning(CreateLogSource("禁言权限"), "禁言权限查询失败：缺少登录账号");
            return false;
        }

        var roomId = RoomId;
        var source = CreateLogSource("禁言权限", roomId);
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
            ModerationStatus = permission.CanModerate ? "" : "当前账号没有该直播间的禁言权限";
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
                log?.Error(source, "禁言权限查询失败", exception);
                ModerationStatus = $"权限查询失败：{exception.Message}";
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
        var source = CreateLogSource("禁言", roomId);
        var accountId = moderationAccountId;
        var cookie = moderationCookie;
        var userUid = long.Parse(item.UserUid);
        var message = item.Content;
        InvalidateModerationPermission();
        var tokenSource = new CancellationTokenSource();
        moderationTokenSource = tokenSource;
        IsMutingDanmu = true;
        ModerationStatus = $"正在禁言 {item.UserName}…";

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
                ModerationStatus = "权限或目标身份已变化，无法禁言该用户";
                log?.Warning(source, $"禁言失败（UID {userUid}）：权限或目标身份已变化");
                return;
            }

            await moderationService.MuteUserAsync(permission.RoomId, userUid, cookie, message, GetModerationTimeout(),
                tokenSource.Token);

            if (IsCurrentModerationOperation(tokenSource, roomId, accountId, cookie))
            {
                log?.Information(source, $"已禁言用户（UID {userUid}）");
                ModerationStatus = $"已禁言 {item.UserName}（UID {userUid}）";
            }
        }
        catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentModerationOperation(tokenSource, roomId, accountId, cookie))
            {
                log?.Error(source, $"禁言失败（UID {userUid}）", exception);
                ModerationStatus = $"禁言失败：{exception.Message}";
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
