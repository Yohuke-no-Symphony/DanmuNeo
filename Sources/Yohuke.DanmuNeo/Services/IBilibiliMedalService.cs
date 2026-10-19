using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>读取粉丝牌并执行账号级佩戴操作。</summary>
public interface IBilibiliMedalService
{
    /// <summary>通过公开房间信息解析真实主播UID。</summary>
    Task<long> GetRoomOwnerUidAsync(string roomId, string? cookie = null, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
    /// <summary>读取经过账号验证的UID对应的牌墙。</summary>
    Task<AccountMedalCollection> GetMedalsAsync(long accountUid, string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
    /// <summary>佩戴指定牌子。</summary>
    Task WearAsync(long medalId, string cookie, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    /// <summary>摘下指定的当前佩戴牌子。</summary>
    Task TakeOffAsync(long medalId, string cookie, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
