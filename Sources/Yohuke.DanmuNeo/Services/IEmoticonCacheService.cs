namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 提供弹幕表情图片的本地缓存。
/// </summary>
public interface IEmoticonCacheService
{
    /// <summary>
    /// 获取表情图片的缓存路径；下载失败时返回空字符串。
    /// </summary>
    Task<string> GetEmoticonAsync(string url, CancellationToken cancellationToken = default);
}
