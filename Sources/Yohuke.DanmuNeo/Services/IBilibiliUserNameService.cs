namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 按 UID 查询 B 站公开用户名。
/// </summary>
public interface IBilibiliUserNameService
{
    /// <summary>
    /// 查询指定用户的公开名称。
    /// </summary>
    Task<string> GetUserNameAsync(long uid, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
