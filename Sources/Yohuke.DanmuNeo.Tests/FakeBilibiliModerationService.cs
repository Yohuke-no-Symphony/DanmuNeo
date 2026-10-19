using System.Threading;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 可控制响应顺序的离线禁言服务。
/// </summary>
public class FakeBilibiliModerationService : IBilibiliModerationService
{
    /// <inheritdoc/>
    public event EventHandler<long>? Changed;

    /// <summary>可控制的房间权限响应，提供时优先于旧的按次响应。</summary>
    public Func<string, string, CancellationToken, Task<RoomModerationPermission>>? PermissionResponseAsync { get; set; }

    /// <summary>已发出的名单请求。</summary>
    public List<(long RoomId, string Cookie, CancellationToken Token)> ListRequests { get; } = [];

    /// <summary>名单响应工厂。</summary>
    public Func<long, string, CancellationToken, Task<IReadOnlyList<ModerationRecord>>> ListResponseAsync { get; set; } =
        (_, _, _) => Task.FromResult<IReadOnlyList<ModerationRecord>>([]);

    /// <summary>已发出的用户搜索。</summary>
    public List<(string Keyword, int Page, CancellationToken Token)> SearchRequests { get; } = [];

    /// <summary>搜索响应工厂。</summary>
    public Func<string, int, CancellationToken, Task<ModerationUserSearchPage>> SearchResponseAsync { get; set; } =
        (_, _, _) => Task.FromResult(new ModerationUserSearchPage());

    /// <summary>已发出的解除请求。</summary>
    public List<(long RoomId, long RecordId, string Cookie)> UnmuteRequests { get; } = [];

    /// <summary>模拟解除错误。</summary>
    public Exception? UnmuteException { get; set; }

    /// <summary>模拟其他入口完成禁言或解除。</summary>
    public void RaiseChanged(long roomId)
    {
        Changed?.Invoke(this, roomId);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ModerationRecord>> GetMutedUsersAsync(long roomId, string cookie,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ListRequests.Add((roomId, cookie, cancellationToken));
        return ListResponseAsync(roomId, cookie, cancellationToken);
    }

    /// <inheritdoc/>
    public Task UnmuteUserAsync(long roomId, long recordId, string cookie,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UnmuteRequests.Add((roomId, recordId, cookie));

        if (UnmuteException is not null)
        {
            return Task.FromException(UnmuteException);
        }

        RaiseChanged(roomId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<ModerationUserSearchPage> SearchUsersAsync(string keyword, string? cookie = null, int page = 1,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        SearchRequests.Add((keyword, page, cancellationToken));
        return SearchResponseAsync(keyword, page, cancellationToken);
    }

    /// <summary>
    /// 权限请求次数。
    /// </summary>
    public int PermissionRequestCount { get; private set; }

    /// <summary>
    /// 禁言请求记录。
    /// </summary>
    public List<(long RoomId, long UserUid, string Cookie, string Message)> MuteRequests { get; } = [];

    /// <summary>
    /// 权限请求令牌。
    /// </summary>
    public List<CancellationToken> PermissionTokens { get; } = [];

    /// <summary>
    /// 权限响应工厂。
    /// </summary>
    public Func<int, Task<RoomModerationPermission>> RespondAsync { get; set; } = _ =>
        Task.FromResult(new RoomModerationPermission
        {
            RoomId = 100,
            AccountUid = 10,
            OwnerUid = 20,
            IsRoomAdmin = true
        });

    /// <summary>
    /// 模拟禁言错误。
    /// </summary>
    public Exception? MuteException { get; set; }

    /// <inheritdoc/>
    public Task<RoomModerationPermission> GetPermissionAsync(
        string roomId, string cookie, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        PermissionTokens.Add(cancellationToken);
        PermissionRequestCount++;
        return PermissionResponseAsync?.Invoke(roomId, cookie, cancellationToken) ?? RespondAsync(PermissionRequestCount);
    }

    /// <inheritdoc/>
    public Task MuteUserAsync(long roomId, long userUid, string cookie, string message, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MuteRequests.Add((roomId, userUid, cookie, message));
        if (MuteException is not null)
        {
            return Task.FromException(MuteException);
        }

        RaiseChanged(roomId);
        return Task.CompletedTask;
    }
}
