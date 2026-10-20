using System.Text.Json;
using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证工具面板逐版加入时不重排已有布局。</summary>
public class ToolPanelLayoutMigrationTests
{
    /// <summary>每个历史版本按冻结的成员集合迁移，主树与浮窗及自定义预设保持原样。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void HistoricalLayoutsOnlyGainHiddenToolPanels(int sourceVersion)
    {
        var members = GetHistoricalMembers(sourceVersion);
        var snapshot = new WorkspaceLayoutSnapshot
        {
            Main = new() { PanelIds = [.. members.Where(id => id != "lyrics")], ActivePanelId = "composer" },
            FloatingWindows =
            [
                new()
                {
                    Layout = new() { PanelIds = ["lyrics"], ActivePanelId = "lyrics" },
                    Bounds = new() { X = -800, Y = 60, Width = 600, Height = 480, Scaling = 2 }
                }
            ],
            FocusedPanelId = "composer"
        };
        var old = new WorkspaceLayoutFile
        {
            Version = sourceVersion,
            CurrentPresetId = "custom",
            CurrentLayout = snapshot,
            UserPresets = [new() { Id = "custom", Name = "我的布局", Layout = snapshot }]
        };

        var upgraded = WorkspaceLayoutSerializer.Deserialize(JsonSerializer.Serialize(old));

        Assert.Equal(WorkspaceLayoutFile.CURRENT_VERSION, upgraded.Version);
        Assert.Equal("custom", upgraded.CurrentPresetId);
        Assert.Equal(JsonSerializer.Serialize(snapshot.Main), JsonSerializer.Serialize(upgraded.CurrentLayout.Main));
        Assert.Equal(JsonSerializer.Serialize(snapshot.FloatingWindows), JsonSerializer.Serialize(upgraded.CurrentLayout.FloatingWindows));
        Assert.Equal(snapshot.FocusedPanelId, upgraded.CurrentLayout.FocusedPanelId);
        Assert.Equal(WorkbenchPanelIds.All.Except(members), upgraded.CurrentLayout.HiddenPanelIds);
        Assert.Contains("liveMarker", upgraded.CurrentLayout.HiddenPanelIds);
        var preset = Assert.Single(upgraded.UserPresets);
        Assert.Equal("我的布局", preset.Name);
        Assert.Equal(upgraded.CurrentLayout.HiddenPanelIds, preset.Layout.HiddenPanelIds);
        Assert.Equal(JsonSerializer.Serialize(snapshot.Main), JsonSerializer.Serialize(preset.Layout.Main));
    }

    /// <summary>旧版本中多出未来面板或缺少原成员时，不能靠升级掩盖损坏。</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(5, false)]
    public void MigrationRejectsInvalidHistoricalMembership(int version, bool futureMember)
    {
        var members = GetHistoricalMembers(version).ToList();
        if (futureMember)
        {
            members.Add("liveMarker");
        }
        else
        {
            members.Remove("composer");
        }
        var old = new WorkspaceLayoutFile
        {
            Version = version,
            CurrentLayout = new() { Main = new() { PanelIds = members } }
        };

        Assert.Throws<InvalidDataException>(() => WorkspaceLayoutSerializer.Deserialize(JsonSerializer.Serialize(old)));
    }

    /// <summary>当前版本再次升级不会新增重复的隐藏记录面板或改变序列化内容。</summary>
    [Fact]
    public void UpgradingAnAlreadyUpgradedLayoutIsIdempotent()
    {
        var snapshot = new WorkspaceLayoutSnapshot
        {
            Main = new() { PanelIds = [.. GetHistoricalMembers(5)], ActivePanelId = "history" },
            FocusedPanelId = "history"
        };
        var old = new WorkspaceLayoutFile
        {
            Version = 5,
            CurrentPresetId = "custom",
            CurrentLayout = snapshot,
            UserPresets = [new() { Id = "custom", Name = "旧版本布局", Layout = snapshot }]
        };
        var upgraded = WorkspaceLayoutMigration.Upgrade(old);

        var repeated = WorkspaceLayoutMigration.Upgrade(upgraded);
        var encoded = WorkspaceLayoutSerializer.Serialize(upgraded);
        var restored = WorkspaceLayoutSerializer.Deserialize(encoded);

        Assert.Same(upgraded, repeated);
        Assert.Equal(6, repeated.Version);
        Assert.Equal(["liveMarker"], restored.CurrentLayout.HiddenPanelIds);
        Assert.Equal(["liveMarker"], Assert.Single(restored.UserPresets).Layout.HiddenPanelIds);
        Assert.Equal(encoded, WorkspaceLayoutSerializer.Serialize(restored));
    }

    /// <summary>历史成员集合保持冻结，旧版校验不能错误接纳新版直播记录面板。</summary>
    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 9)]
    [InlineData(3, 10)]
    [InlineData(4, 11)]
    [InlineData(5, 12)]
    public void HistoricalVersionValidationUsesFrozenPanelMembership(int version, int count)
    {
        var members = GetHistoricalMembers(version);
        Assert.Equal(count, WorkbenchPanelIds.ForVersion(version).Count);
        Assert.Equal(members, WorkbenchPanelIds.ForVersion(version));
        var snapshot = new WorkspaceLayoutSnapshot { HiddenPanelIds = [.. members] };
        Assert.True(WorkspaceLayoutValidator.TryValidate(snapshot, out var error, out _, version), error);
        Assert.False(WorkspaceLayoutValidator.TryValidate(snapshot, out _));
        if (version == 1)
        {
            Assert.True(WorkspaceLayoutValidator.TryValidate(snapshot, out error, legacy: true), error);
        }
        snapshot.HiddenPanelIds.Add(WorkbenchPanelIds.LIVE_MARKER);
        Assert.False(WorkspaceLayoutValidator.TryValidate(snapshot, out _, out _, version));
    }

    /// <summary>早期布局中的隐藏顺序及多个自定义预设分别保留，只追加后续版本的新面板。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UpgradePreservesExistingHiddenPanelsAndEveryUserPreset(int version)
    {
        var members = GetHistoricalMembers(version);
        var selected = new WorkspaceLayoutSnapshot
        {
            Main = new() { PanelIds = [.. members.Except(["workspace"])], ActivePanelId = "history" },
            HiddenPanelIds = ["workspace"],
            FocusedPanelId = "history"
        };
        var other = new WorkspaceLayoutSnapshot
        {
            Main = new() { PanelIds = [.. members.Except(["lyrics", "forwarding"])], ActivePanelId = "composer" },
            HiddenPanelIds = ["forwarding", "lyrics"],
            FocusedPanelId = "composer"
        };
        var original = new WorkspaceLayoutFile
        {
            Version = version,
            CurrentPresetId = "second",
            CurrentLayout = selected,
            UserPresets =
            [
                new() { Id = "first", Name = "第一个预设", Layout = other },
                new() { Id = "second", Name = "第二个预设", Layout = selected }
            ]
        };

        var upgraded = WorkspaceLayoutSerializer.Deserialize(JsonSerializer.Serialize(original));

        var additions = WorkbenchPanelIds.All.Except(members).ToArray();
        Assert.Equal("second", upgraded.CurrentPresetId);
        Assert.Equal(selected.HiddenPanelIds.Concat(additions), upgraded.CurrentLayout.HiddenPanelIds);
        Assert.Equal(2, upgraded.UserPresets.Count);
        for (var index = 0; index < original.UserPresets.Count; index++)
        {
            var before = original.UserPresets[index];
            var after = upgraded.UserPresets[index];
            Assert.Equal(before.Id, after.Id);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(JsonSerializer.Serialize(before.Layout.Main), JsonSerializer.Serialize(after.Layout.Main));
            Assert.Equal(before.Layout.FocusedPanelId, after.Layout.FocusedPanelId);
            Assert.Equal(before.Layout.HiddenPanelIds.Concat(additions), after.Layout.HiddenPanelIds);
        }
    }

    private static string[] GetHistoricalMembers(int version)
    {
        return version switch
        {
            1 => ["workspace", "livePlayer", "composer", "superChat", "danmu", "history", "lyrics", "forwarding"],
            2 => ["workspace", "livePlayer", "composer", "superChat", "danmu", "history", "lyrics", "forwarding", "log"],
            3 => ["workspace", "livePlayer", "composer", "superChat", "danmu", "history", "lyrics", "forwarding", "log", "moderation"],
            4 => ["workspace", "livePlayer", "composer", "superChat", "danmu", "history", "lyrics", "forwarding", "log", "moderation", "accountMedals"],
            5 => ["workspace", "livePlayer", "composer", "superChat", "danmu", "history", "lyrics", "forwarding", "log", "moderation", "accountMedals", "liveCaptures"],
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
    }
}
