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
        Assert.Contains("accountMedals", upgraded.CurrentLayout.HiddenPanelIds);
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
    public void MigrationRejectsInvalidHistoricalMembership(int version, bool futureMember)
    {
        var members = GetHistoricalMembers(version).ToList();
        if (futureMember)
        {
            members.Add("accountMedals");
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

    private static string[] GetHistoricalMembers(int version)
    {
        return version switch
        {
            1 => ["workspace", "livePlayer", "composer", "superChat", "danmu", "history", "lyrics", "forwarding"],
            2 => ["workspace", "livePlayer", "composer", "superChat", "danmu", "history", "lyrics", "forwarding", "log"],
            3 => ["workspace", "livePlayer", "composer", "superChat", "danmu", "history", "lyrics", "forwarding", "log", "moderation"],
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
    }
}
