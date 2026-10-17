using Yohuke.DanmuNeo.I18n;
using Avalonia.Input;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 快捷键绑定服务。
/// </summary>
public static class ShortcutBindingService
{
    private const int CURRENT_SHORTCUT_BINDINGS_VERSION = 2;

    private static readonly HashSet<string> MODIFIER_KEY_NAMES =
    [
        "LeftShift",
        "RightShift",
        "LeftCtrl",
        "RightCtrl",
        "LeftAlt",
        "RightAlt",
        "LWin",
        "RWin",
        "Apps"
    ];

    private static readonly IReadOnlyDictionary<string, string> LEGACY_OPTION_DEFAULT_GESTURES =
        new Dictionary<string, string>
        {
            [ShortcutActionKeys.LIVE_ROOM_START_LISTENING] = "Alt+Shift+L",
            [ShortcutActionKeys.LIVE_ROOM_STOP_LISTENING] = "Alt+Shift+K",
            [ShortcutActionKeys.LIVE_PLAYER_START] = "Alt+Shift+P",
            [ShortcutActionKeys.LIVE_PLAYER_STOP] = "Alt+Shift+O",
            [ShortcutActionKeys.LIVE_PLAYER_CHASE] = "Alt+Shift+R",
            [ShortcutActionKeys.INPUT_FOCUS_DRAFT] = "Alt+I",
            [ShortcutActionKeys.INPUT_CLEAR_DRAFT] = "Alt+Shift+Backspace",
            [ShortcutActionKeys.LYRIC_START_SENDING] = "Alt+Shift+A",
            [ShortcutActionKeys.LYRIC_STOP_SENDING] = "Alt+Shift+S",
            [ShortcutActionKeys.LYRIC_SEEK_BACKWARD] = "Alt+Left",
            [ShortcutActionKeys.LYRIC_SEEK_FORWARD] = "Alt+Right"
        };

    /// <summary>
    /// 归一化应用快捷键设置。
    /// </summary>
    public static void Normalize(AppSettings settings)
    {
        settings.ShortcutBindings ??= [];
        NormalizeBindingsInPlace(settings.ShortcutBindings, settings.ShortcutBindingsVersion);
        settings.ShortcutBindingsVersion = CURRENT_SHORTCUT_BINDINGS_VERSION;
    }

    /// <summary>
    /// 归一化快捷键绑定列表。
    /// </summary>
    public static List<ShortcutBindingState> NormalizeBindings(IEnumerable<ShortcutBindingState>? bindings)
    {
        return NormalizeBindings(bindings, CURRENT_SHORTCUT_BINDINGS_VERSION);
    }

    /// <summary>
    /// 归一化指定版本的快捷键绑定列表。
    /// </summary>
    public static List<ShortcutBindingState> NormalizeBindings(
        IEnumerable<ShortcutBindingState>? bindings,
        int shortcutBindingsVersion)
    {
        return NormalizeBindingsCore(bindings, shortcutBindingsVersion, false);
    }

    private static void NormalizeBindingsInPlace(
        List<ShortcutBindingState> bindings,
        int shortcutBindingsVersion)
    {
        var normalizedBindings = NormalizeBindingsCore(bindings, shortcutBindingsVersion, true);
        bindings.Clear();
        bindings.AddRange(normalizedBindings);
    }

    private static List<ShortcutBindingState> NormalizeBindingsCore(
        IEnumerable<ShortcutBindingState>? bindings,
        int shortcutBindingsVersion,
        bool reuseExistingBindings)
    {
        var bindingByActionKey = (bindings ?? [])
            .Where(binding => !string.IsNullOrWhiteSpace(binding.ActionKey))
            .GroupBy(binding => binding.ActionKey)
            .ToDictionary(group => group.Key, group => group.Last());
        var actionKeys = ShortcutActionCatalog.Actions.Select(action => action.ActionKey).ToHashSet();
        var normalizedBindings = new List<ShortcutBindingState>();

        foreach (var definition in ShortcutActionCatalog.Actions)
        {
            if (!bindingByActionKey.TryGetValue(definition.ActionKey, out var binding))
            {
                normalizedBindings.Add(CreateOrUpdateBinding(
                    null,
                    definition.ActionKey,
                    definition.DefaultGestureText,
                    true,
                    reuseExistingBindings));
                continue;
            }

            if (!actionKeys.Contains(binding.ActionKey))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(binding.GestureText) && !binding.IsEnabled)
            {
                normalizedBindings.Add(CreateOrUpdateBinding(
                    binding,
                    definition.ActionKey,
                    "",
                    false,
                    reuseExistingBindings));
                continue;
            }

            var gestureText = ShouldMigrateLegacyDefault(definition, binding, shortcutBindingsVersion)
                ? definition.DefaultGestureText
                : binding.GestureText;

            normalizedBindings.Add(CreateOrUpdateBinding(
                binding,
                definition.ActionKey,
                TryNormalizeGestureText(gestureText, out var normalizedGestureText)
                    ? normalizedGestureText
                    : definition.DefaultGestureText,
                binding.IsEnabled,
                reuseExistingBindings));
        }

        return normalizedBindings;
    }

    private static ShortcutBindingState CreateOrUpdateBinding(
        ShortcutBindingState? binding,
        string actionKey,
        string gestureText,
        bool isEnabled,
        bool reuseExistingBinding)
    {
        var target = reuseExistingBinding && binding is not null ? binding : new();
        target.ActionKey = actionKey;
        target.GestureText = gestureText;
        target.IsEnabled = isEnabled;
        return target;
    }

    private static bool ShouldMigrateLegacyDefault(
        ShortcutActionDefinition definition,
        ShortcutBindingState binding,
        int shortcutBindingsVersion)
    {
        if (shortcutBindingsVersion >= CURRENT_SHORTCUT_BINDINGS_VERSION ||
            !LEGACY_OPTION_DEFAULT_GESTURES.TryGetValue(definition.ActionKey, out var legacyGestureText) ||
            !TryNormalizeGestureText(binding.GestureText, out var gestureText) ||
            !TryNormalizeGestureText(legacyGestureText, out var normalizedLegacyGestureText))
        {
            return false;
        }

        return string.Equals(gestureText, normalizedLegacyGestureText, StringComparison.Ordinal);
    }

    /// <summary>
    /// 获取冲突的动作键。
    /// </summary>
    public static HashSet<string> GetConflictActionKeys(IEnumerable<ShortcutBindingState> bindings)
    {
        return bindings
            .Where(binding => binding.IsEnabled && TryNormalizeGestureText(binding.GestureText, out _))
            .GroupBy(binding => NormalizeGestureText(binding.GestureText))
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(binding => binding.ActionKey))
            .ToHashSet();
    }

    /// <summary>
    /// 获取按键触发的动作键。
    /// </summary>
    public static string? GetTriggeredActionKey(
        IEnumerable<ShortcutBindingState> bindings,
        Key key,
        KeyModifiers modifiers)
    {
        if (!TryCreateGestureText(key, modifiers, out var currentGestureText))
        {
            return null;
        }

        var conflictActionKeys = GetConflictActionKeys(bindings);

        foreach (var binding in bindings)
        {
            if (!binding.IsEnabled ||
                conflictActionKeys.Contains(binding.ActionKey) ||
                !TryNormalizeGestureText(binding.GestureText, out var gestureText) ||
                !string.Equals(gestureText, currentGestureText, StringComparison.Ordinal))
            {
                continue;
            }

            return binding.ActionKey;
        }

        return null;
    }

    /// <summary>
    /// 尝试从按键创建快捷键文本。
    /// </summary>
    public static bool TryCreateGestureText(Key key, KeyModifiers modifiers, out string gestureText)
    {
        gestureText = "";

        if (key == Key.None || MODIFIER_KEY_NAMES.Contains(key.ToString()))
        {
            return false;
        }

        gestureText = FormatGesture(new(key, modifiers));
        return true;
    }

    /// <summary>
    /// 尝试归一化快捷键文本。
    /// </summary>
    public static bool TryNormalizeGestureText(string? text, out string gestureText)
    {
        gestureText = "";

        if (!TryParse(text, out var gesture))
        {
            return false;
        }

        gestureText = FormatGesture(gesture);
        return true;
    }

    /// <summary>
    /// 格式化显示快捷键文本。
    /// </summary>
    public static string FormatDisplayText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Localization.Get("Services.Shortcut.NotSet");
        }

        return TryNormalizeGestureText(text, out var gestureText) ? gestureText : Localization.Get("Services.Shortcut.Invalid");
    }

    private static ShortcutBindingState CreateDefaultBinding(ShortcutActionDefinition definition)
    {
        return new()
        {
            ActionKey = definition.ActionKey,
            GestureText = definition.DefaultGestureText,
            IsEnabled = true
        };
    }

    private static string NormalizeGestureText(string text)
    {
        return TryNormalizeGestureText(text, out var gestureText) ? gestureText : "";
    }

    private static bool TryParse(string? text, out ShortcutGesture gesture)
    {
        gesture = new(Key.None, KeyModifiers.None);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var key = Key.None;
        var modifiers = KeyModifiers.None;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (TryParseModifier(part, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (key != Key.None || !TryParseKey(part, out key))
            {
                return false;
            }
        }

        if (key == Key.None || MODIFIER_KEY_NAMES.Contains(key.ToString()))
        {
            return false;
        }

        gesture = new(key, modifiers);
        return true;
    }

    private static bool TryParseModifier(string text, out KeyModifiers modifier)
    {
        modifier = KeyModifiers.None;

        switch (text.Trim().ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                modifier = KeyModifiers.Control;
                return true;
            case "alt":
            case "option":
                modifier = KeyModifiers.Alt;
                return true;
            case "shift":
                modifier = KeyModifiers.Shift;
                return true;
            case "meta":
            case "cmd":
            case "command":
                modifier = KeyModifiers.Meta;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseKey(string text, out Key key)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "esc":
                key = Key.Escape;
                return true;
            case "backspace":
                key = Key.Back;
                return true;
            default:
                return Enum.TryParse(text, true, out key);
        }
    }

    private static string FormatGesture(ShortcutGesture gesture)
    {
        var parts = new List<string>();

        if (gesture.Modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (gesture.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (gesture.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (gesture.Modifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("Meta");
        }

        parts.Add(FormatKey(gesture.Key));
        return string.Join("+", parts);
    }

    private static string FormatKey(Key key)
    {
        return key switch
        {
            Key.Escape => "Esc",
            Key.Back => "Backspace",
            _ => key.ToString()
        };
    }
}
