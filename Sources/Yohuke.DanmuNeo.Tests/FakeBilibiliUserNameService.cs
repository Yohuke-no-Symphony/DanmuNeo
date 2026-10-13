using System.Threading;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 可控制响应和取消行为的离线用户名查询服务。
/// </summary>
public class FakeBilibiliUserNameService : IBilibiliUserNameService
{
    /// <summary>
    /// 查询的 UID。
    /// </summary>
    public List<long> RequestedUids { get; } = [];

    /// <summary>
    /// 各次查询的取消令牌。
    /// </summary>
    public List<CancellationToken> RequestTokens { get; } = [];

    /// <summary>
    /// 每次查询使用的响应函数。
    /// </summary>
    public Func<long, CancellationToken, Task<string>> RespondAsync { get; set; } =
        (uid, _) => Task.FromResult($"用户{uid}");

    /// <inheritdoc/>
    public Task<string> GetUserNameAsync(long uid, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        RequestedUids.Add(uid);
        RequestTokens.Add(cancellationToken);
        return RespondAsync(uid, cancellationToken);
    }
}
