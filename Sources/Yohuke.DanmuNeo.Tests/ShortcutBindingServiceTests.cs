using Avalonia.Input;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

public class ShortcutBindingServiceTests
{
    /// <summary>
    /// 旧配置补齐下一句默认绑定，并保留既有自定义绑定对象。
    /// </summary>
    [Fact]
    public void NormalizeAddsNextLyricBindingWithoutChangingCustomBinding()
    {
        var existing = new ShortcutBindingState
        {
            ActionKey = ShortcutActionKeys.LYRIC_SEEK_FORWARD, GestureText = "Ctrl+F", IsEnabled = true
        };
        var settings = new AppSettings { ShortcutBindingsVersion = 2, ShortcutBindings = [existing] };

        ShortcutBindingService.Normalize(settings);

        Assert.Same(existing, settings.ShortcutBindings.Single(item => item.ActionKey == existing.ActionKey));
        Assert.Equal("Ctrl+F", existing.GestureText);
        var next = Assert.Single(settings.ShortcutBindings, item => item.ActionKey == ShortcutActionKeys.LYRIC_SEEK_NEXT);
        Assert.True(next.IsEnabled);
        Assert.Equal("Ctrl+Shift+Down", next.GestureText);
        Assert.Equal(ShortcutActionKeys.LYRIC_SEEK_NEXT,
            ShortcutBindingService.GetTriggeredActionKey(settings.ShortcutBindings, Key.Down,
                KeyModifiers.Control | KeyModifiers.Shift));
        Assert.Empty(ShortcutBindingService.GetConflictActionKeys(settings.ShortcutBindings));
    }

    /// <summary>
    /// 下一句的禁用、清空和自定义手势经保存读取后仍然保留。
    /// </summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("Ctrl+N", false)]
    [InlineData("Ctrl+N", true)]
    public void NextLyricBindingPersistsCustomAndDisabledStates(string gesture, bool enabled)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var service = new AppStateService(directory);
            service.Save(new()
            {
                Settings = new()
                {
                    ShortcutBindingsVersion = 2,
                    ShortcutBindings =
                    [
                        new() { ActionKey = ShortcutActionKeys.LYRIC_SEEK_NEXT, GestureText = gesture, IsEnabled = enabled }
                    ]
                }
            });
            var binding = service.Load().Settings.ShortcutBindings
                .Single(item => item.ActionKey == ShortcutActionKeys.LYRIC_SEEK_NEXT);

            Assert.Equal(gesture, binding.GestureText);
            Assert.Equal(enabled, binding.IsEnabled);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DefaultBindingsCoverEveryActionAndCanParse()
    {
        var bindings = ShortcutBindingService.NormalizeBindings(null);

        Assert.Equal(ShortcutActionCatalog.Actions.Count, bindings.Count);

        foreach (var action in ShortcutActionCatalog.Actions)
        {
            var binding = Assert.Single(bindings, item => item.ActionKey == action.ActionKey);
            Assert.True(binding.IsEnabled);
            Assert.True(ShortcutBindingService.TryNormalizeGestureText(binding.GestureText, out _));
        }
    }

    [Fact]
    public void NormalizeBindingsFillsMissingActions()
    {
        var bindings = ShortcutBindingService.NormalizeBindings(
        [
            new()
            {
                ActionKey = ShortcutActionKeys.INPUT_FOCUS_DRAFT,
                GestureText = "Alt+I",
                IsEnabled = true
            }
        ]);

        Assert.Equal(ShortcutActionCatalog.Actions.Count, bindings.Count);
        Assert.Contains(bindings, binding => binding.ActionKey == ShortcutActionKeys.LIVE_ROOM_START_LISTENING);
    }

    [Fact]
    public void NormalizeBindingsKeepsClearedDisabledBinding()
    {
        var bindings = ShortcutBindingService.NormalizeBindings(
        [
            new()
            {
                ActionKey = ShortcutActionKeys.INPUT_CLEAR_DRAFT,
                GestureText = "",
                IsEnabled = false
            }
        ]);
        var binding = Assert.Single(bindings, item => item.ActionKey == ShortcutActionKeys.INPUT_CLEAR_DRAFT);

        Assert.Equal("", binding.GestureText);
        Assert.False(binding.IsEnabled);
    }

    [Fact]
    public void NormalizeBindingsMigratesLegacyOptionDefaults()
    {
        var bindings = ShortcutBindingService.NormalizeBindings(
        [
            new()
            {
                ActionKey = ShortcutActionKeys.INPUT_FOCUS_DRAFT,
                GestureText = "Alt+I",
                IsEnabled = true
            }
        ], 0);
        var binding = Assert.Single(bindings, item => item.ActionKey == ShortcutActionKeys.INPUT_FOCUS_DRAFT);
        var defaultGestureText = ShortcutActionCatalog.Actions
            .Single(action => action.ActionKey == ShortcutActionKeys.INPUT_FOCUS_DRAFT)
            .DefaultGestureText;

        Assert.Equal(defaultGestureText, binding.GestureText);
    }

    [Fact]
    public void NormalizeBindingsKeepsCustomGestureDuringMigration()
    {
        var bindings = ShortcutBindingService.NormalizeBindings(
        [
            new()
            {
                ActionKey = ShortcutActionKeys.INPUT_FOCUS_DRAFT,
                GestureText = "Ctrl+I",
                IsEnabled = true
            }
        ], 0);
        var binding = Assert.Single(bindings, item => item.ActionKey == ShortcutActionKeys.INPUT_FOCUS_DRAFT);

        Assert.Equal("Ctrl+I", binding.GestureText);
    }

    [Fact]
    public void NormalizeSettingsKeepsKnownBindingReferences()
    {
        var binding = new ShortcutBindingState
        {
            ActionKey = ShortcutActionKeys.INPUT_FOCUS_DRAFT,
            GestureText = "Alt+I",
            IsEnabled = true
        };
        var settings = new AppSettings
        {
            ShortcutBindings = [binding]
        };
        var defaultGestureText = ShortcutActionCatalog.Actions
            .Single(action => action.ActionKey == ShortcutActionKeys.INPUT_FOCUS_DRAFT)
            .DefaultGestureText;

        ShortcutBindingService.Normalize(settings);
        var normalizedBinding = Assert.Single(
            settings.ShortcutBindings,
            item => item.ActionKey == ShortcutActionKeys.INPUT_FOCUS_DRAFT);

        Assert.Same(binding, normalizedBinding);
        Assert.Equal(defaultGestureText, normalizedBinding.GestureText);
    }

    [Fact]
    public void NormalizeBindingsFallsBackFromInvalidGesture()
    {
        var bindings = ShortcutBindingService.NormalizeBindings(
        [
            new()
            {
                ActionKey = ShortcutActionKeys.LIVE_PLAYER_CHASE,
                GestureText = "NotAKey",
                IsEnabled = true
            }
        ]);
        var binding = Assert.Single(bindings, item => item.ActionKey == ShortcutActionKeys.LIVE_PLAYER_CHASE);
        var defaultGestureText = ShortcutActionCatalog.Actions
            .Single(action => action.ActionKey == ShortcutActionKeys.LIVE_PLAYER_CHASE)
            .DefaultGestureText;

        Assert.Equal(defaultGestureText, binding.GestureText);
    }

    [Fact]
    public void DuplicateEnabledGesturesAreConflictedAndDoNotTrigger()
    {
        var bindings = ShortcutBindingService.NormalizeBindings(
        [
            new()
            {
                ActionKey = ShortcutActionKeys.LIVE_ROOM_START_LISTENING,
                GestureText = "Alt+I",
                IsEnabled = true
            },
            new()
            {
                ActionKey = ShortcutActionKeys.INPUT_FOCUS_DRAFT,
                GestureText = "Alt+I",
                IsEnabled = true
            }
        ]);

        var conflicts = ShortcutBindingService.GetConflictActionKeys(bindings);
        var triggeredActionKey = ShortcutBindingService.GetTriggeredActionKey(bindings, Key.I, KeyModifiers.Alt);

        Assert.Contains(ShortcutActionKeys.LIVE_ROOM_START_LISTENING, conflicts);
        Assert.Contains(ShortcutActionKeys.INPUT_FOCUS_DRAFT, conflicts);
        Assert.Null(triggeredActionKey);
    }
}
