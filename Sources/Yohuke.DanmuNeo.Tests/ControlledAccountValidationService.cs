using System.Threading;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 以手动完成的结果模拟乱序登录校验，不访问真实接口。
/// </summary>
public sealed class ControlledAccountValidationService : IBilibiliAccountValidationService
{
    /// <summary>
    /// 已发出的校验请求。
    /// </summary>
    public List<TaskCompletionSource<AccountValidationResult>> Requests { get; } = [];

    /// <inheritdoc/>
    public Task<AccountValidationResult> ValidateAsync(string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var response = new TaskCompletionSource<AccountValidationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Requests.Add(response);
        return response.Task;
    }
}
