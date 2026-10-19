using System.Text.Json.Nodes;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

/// <summary>在读取当前与旧版存储时迁移发送设置键名。</summary>
public static class SendingSettingsMigration
{
    /// <summary>迁移设置或旧整包状态，保留新键的明确选择。</summary>
    public static string Migrate<T>(string json)
    {
        if (typeof(T) != typeof(AppSettings) && typeof(T) != typeof(AppState))
        {
            return json;
        }
        var root = JsonNode.Parse(json) as JsonObject;
        var settings = typeof(T) == typeof(AppSettings) ? root :
            root?.FirstOrDefault(pair => pair.Key.Equals("Settings", StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
        if (settings is null)
        {
            return json;
        }
        var oldKey = settings.Select(pair => pair.Key).FirstOrDefault(key => key.Equals("ResendWhenShielded", StringComparison.OrdinalIgnoreCase));
        if (oldKey is null)
        {
            return json;
        }
        if (!settings.Any(pair => pair.Key.Equals("RetryWhenRateLimited", StringComparison.OrdinalIgnoreCase)))
        {
            settings["RetryWhenRateLimited"] = settings[oldKey]?.DeepClone();
        }
        settings.Remove(oldKey);
        return root!.ToJsonString();
    }
}
