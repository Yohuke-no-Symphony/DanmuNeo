namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 提供应用目录路径。
/// </summary>
public class AppDirectoryService
{
    /// <summary>
    /// 初始化应用目录服务。
    /// </summary>
    public AppDirectoryService(string? configDirectory = null)
    {
        ConfigDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Yohuke.DanmuNeo");
        DanmuLogsDirectory = Path.Combine(ConfigDirectory, "DanmuLogs");
        CacheDirectory = Path.Combine(ConfigDirectory, "Cache");
        AvatarCacheDirectory = Path.Combine(CacheDirectory, "Avatars");
        EmoticonCacheDirectory = Path.Combine(ConfigDirectory, "temp", "emots");
    }

    /// <summary>
    /// 配置目录。
    /// </summary>
    public string ConfigDirectory { get; }

    /// <summary>收到消息的永久留档目录，不属于缓存。</summary>
    public string DanmuLogsDirectory { get; }

    /// <summary>
    /// 缓存目录。
    /// </summary>
    public string CacheDirectory { get; }

    /// <summary>
    /// 主播头像缓存目录。
    /// </summary>
    public string AvatarCacheDirectory { get; }

    /// <summary>
    /// 弹幕表情图片缓存目录。
    /// </summary>
    public string EmoticonCacheDirectory { get; }

    /// <summary>
    /// 确保应用目录存在。
    /// </summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(AvatarCacheDirectory);
        Directory.CreateDirectory(EmoticonCacheDirectory);
    }
}
