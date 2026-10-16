using System.Threading;
using Yohuke.DanmuNeo.Models.Updates;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用受控响应验证启动检查，允许模拟忽略取消的迟到请求。
/// </summary>
public sealed class FakeAppUpdateService : IAppUpdateService
{
    public TaskCompletionSource<AppUpdateInfo?> Response { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }
    public Version? CurrentVersion { get; private set; }
    public CancellationToken CancellationToken { get; private set; }

    /// <inheritdoc/>
    public Task<AppUpdateInfo?> CheckForUpdateAsync(Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        CurrentVersion = currentVersion;
        CancellationToken = cancellationToken;
        return Response.Task;
    }
}