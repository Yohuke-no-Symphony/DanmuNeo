using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Logging;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 记录应用启动过程。
/// </summary>
public static class StartupLog
{
    private static readonly object LOCKER = new();
    private static bool isInitialized;

    internal static event Action<AppLogLevel, string, string, Exception?>? Written;

    /// <summary>
    /// 追加启动日志。
    /// </summary>
    public static void Append(string message, AppLogLevel level = AppLogLevel.INFO,
        string? source = null, Exception? error = null)
    {
        var safeMessage = AppLogSanitizer.Sanitize(message);

        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Yohuke.DanmuNeo");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "startup.log");

            lock (LOCKER)
            {
                if (!isInitialized)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    isInitialized = true;
                }

                var details = error is null ? "" : Environment.NewLine + AppLogSanitizer.Sanitize(error.ToString());
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {level} {safeMessage}{details}{Environment.NewLine}");
            }
        }
        catch
        {
        }

        var sourceName = source ?? (Localization.IsInitialized ? Localization.Get("Services.Log.Startup") : "Startup");
        Written?.Invoke(level, sourceName, safeMessage, error);
    }
}
