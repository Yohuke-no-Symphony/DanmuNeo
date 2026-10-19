using Yohuke.DanmuNeo.I18n;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 将 B 站分组弹幕选项和用户当前设置转换为配置快照。
/// </summary>
public static class BilibiliDanmuConfigSelector
{
    /// <summary>
    /// 过滤不可用和损坏的选项，并验证服务端的当前设置。
    /// </summary>
    public static DanmuRoomConfig Select(BilibiliDanmuConfigData? config, BilibiliUserInfoData? user)
    {
        if (config?.Group is not JArray groups || config.Mode is not JArray modeItems)
        {
            throw LocalizedExceptionText.Create("Services.BilibiliDanmuConfig.OptionsMissing",
                message => new InvalidOperationException(message));
        }

        var danmu = (user?.Property as JObject)?["danmu"] as JObject;

        if (!TryReadInteger(danmu?["color"], out var currentColor) ||
            !TryReadInteger(danmu?["mode"], out var currentMode))
        {
            throw LocalizedExceptionText.Create("Services.BilibiliDanmuConfig.CurrentSettingsMissing",
                message => new InvalidOperationException(message));
        }

        var current = new DanmuSendOptions { Color = currentColor, Mode = currentMode };

        if (!current.IsValid)
        {
            throw LocalizedExceptionText.Create("Services.BilibiliDanmuConfig.CurrentSettingsInvalid",
                message => new InvalidOperationException(message));
        }

        var colors = new List<DanmuColorOption>();
        var colorValues = new HashSet<int>();

        foreach (var group in groups.OfType<JObject>())
        {
            if (group["color"] is not JArray colorItems)
            {
                continue;
            }

            foreach (var item in colorItems.OfType<JObject>())
            {
                if (!IsAvailable(item["status"]) || !TryReadColor(item["color_hex"], out var color) ||
                    !colorValues.Add(color))
                {
                    continue;
                }

                colors.Add(new()
                {
                    Value = color,
                    Name = ReadName(item, $"#{color.ToString("X6", CultureInfo.InvariantCulture)}")
                });
            }
        }

        var modes = new List<DanmuModeOption>();
        var modeValues = new HashSet<int>();

        foreach (var item in modeItems.OfType<JObject>())
        {
            if (!IsAvailable(item["status"]) || !TryReadInteger(item["mode"], out var mode) || mode <= 0 ||
                !modeValues.Add(mode))
            {
                continue;
            }

            modes.Add(new() { Value = mode, Name = ReadName(item, mode.ToString(CultureInfo.InvariantCulture)) });
        }

        var length = TryReadInteger(danmu?["length"], out var userLength) && userLength > 0
            ? userLength
            : Math.Max(0, config.Length);
        return new() { Colors = colors.AsReadOnly(), Modes = modes.AsReadOnly(), Current = current, Length = length };
    }

    private static bool IsAvailable(JToken? status)
    {
        return status?.Type switch
        {
            JTokenType.Boolean => status.Value<bool>(),
            JTokenType.Integer => status.Value<long>() != 0,
            _ => false
        };
    }

    private static bool TryReadInteger(JToken? token, out int value)
    {
        return int.TryParse(token?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadColor(JToken? token, out int color)
    {
        color = 0;

        if (token is not { Type: JTokenType.String })
        {
            return false;
        }

        var text = token.Value<string>()?.Trim() ?? "";

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }
        else if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        return text.Length is > 0 and <= 6 &&
               int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out color);
    }

    private static string ReadName(JObject item, string fallback)
    {
        var name = item["name"]?.Value<string>();
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }
}
