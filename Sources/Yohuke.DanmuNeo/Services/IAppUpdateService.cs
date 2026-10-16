using Yohuke.DanmuNeo.Models.Updates;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 检查是否存在高于当前版本的应用正式发布。
/// </summary>
public interface IAppUpdateService
{
    /// <summary>
    /// 获取可用更新；没有更高的正式版本时返回空。
    /// </summary>
    Task<AppUpdateInfo?> CheckForUpdateAsync(Version currentVersion, CancellationToken cancellationToken = default);
}