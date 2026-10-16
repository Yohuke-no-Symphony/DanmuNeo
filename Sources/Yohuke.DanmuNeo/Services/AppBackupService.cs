using System.Diagnostics;
using System.IO.Compression;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 提供配置备份、缓存统计和目录打开能力。
/// </summary>
public class AppBackupService
{
    private static readonly string[] BACKUP_FILE_NAMES =
    [
        "settings.json",
        "layouts.json",
        "accounts.json",
        "workspaces.json",
        "lyric-library.json",
        "state.json",
        "startup.log"
    ];

    private readonly AppDirectoryService directoryService;

    /// <summary>
    /// 初始化备份服务。
    /// </summary>
    public AppBackupService(AppDirectoryService? directoryService = null)
    {
        this.directoryService = directoryService ?? new();
    }

    /// <summary>
    /// 计算目录体积。
    /// </summary>
    public long GetDirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Sum(filePath =>
            {
                try
                {
                    return new FileInfo(filePath).Length;
                }
                catch
                {
                    return 0;
                }
            });
    }

    /// <summary>
    /// 计算头像与表情缓存的总体积。
    /// </summary>
    public long GetCacheSize()
    {
        return GetDirectorySize(directoryService.CacheDirectory) +
               GetDirectorySize(directoryService.EmoticonCacheDirectory);
    }

    /// <summary>
    /// 清理缓存目录。
    /// </summary>
    public void ClearCache()
    {
        if (Directory.Exists(directoryService.CacheDirectory))
        {
            Directory.Delete(directoryService.CacheDirectory, true);
        }

        if (Directory.Exists(directoryService.EmoticonCacheDirectory))
        {
            Directory.Delete(directoryService.EmoticonCacheDirectory, true);
        }

        directoryService.EnsureDirectories();
    }

    /// <summary>
    /// 导出配置、工作区、歌词库、屏蔽词替换库和备份日志为 zip。
    /// </summary>
    public async Task<string> ExportBackupZipAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var filePath = Path.Combine(outputDirectory, $"Yohuke.DanmuNeo-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        await Task.Run(() =>
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);

            foreach (var fileName in BACKUP_FILE_NAMES)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddFileIfExists(archive, Path.Combine(directoryService.ConfigDirectory, fileName), fileName);
            }

            AddDirectoryIfExists(archive, Path.Combine(directoryService.ConfigDirectory, "Backups"), "Backups", cancellationToken);
            AddDirectoryIfExists(archive, Path.Combine(directoryService.ConfigDirectory, "Corrupt"), "Corrupt", cancellationToken);
        }, cancellationToken);

        return filePath;
    }

    /// <summary>
    /// 打开目录。
    /// </summary>
    public void OpenDirectory(string directory)
    {
        Directory.CreateDirectory(directory);

        if (OperatingSystem.IsMacOS())
        {
            StartProcessWithArgument("open", directory);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            StartProcessWithArgument("explorer", directory);
            return;
        }

        StartProcessWithArgument("xdg-open", directory);
    }

    /// <summary>
    /// 格式化字节体积。
    /// </summary>
    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{bytes} {units[unitIndex]}" : $"{value:0.##} {units[unitIndex]}";
    }

    private static void AddFileIfExists(ZipArchive archive, string filePath, string entryName)
    {
        if (File.Exists(filePath))
        {
            archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
        }
    }

    private static void StartProcessWithArgument(string fileName, string argument)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(argument);
        Process.Start(startInfo);
    }

    private static void AddDirectoryIfExists(
        ZipArchive archive,
        string directory,
        string entryPrefix,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(directory, filePath);
            var entryName = Path.Combine(entryPrefix, relativePath).Replace(Path.DirectorySeparatorChar, '/');
            AddFileIfExists(archive, filePath, entryName);
        }
    }
}
