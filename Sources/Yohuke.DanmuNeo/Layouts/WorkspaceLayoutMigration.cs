using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Layouts;

internal static class WorkspaceLayoutMigration
{
    private const double DEFAULT_SIDEBAR_PROPORTION = 0.2;
    private const double DEFAULT_LIVE_PROPORTION = 0.625;
    private const double DEFAULT_TOOL_PROPORTION = 0.375;

    internal static WorkspaceLayoutFile Upgrade(WorkspaceLayoutFile file)
    {
        if (file.Version != 1)
        {
            return file;
        }

        if (file.UserPresets is null || file.UserPresets.Any(preset => preset is null))
        {
            throw new InvalidDataException("布局文件缺少有效的自定义布局集合。");
        }

        return file with
        {
            Version = WorkspaceLayoutFile.CURRENT_VERSION,
            CurrentLayout = AddLogPanel(file.CurrentLayout),
            UserPresets = file.UserPresets.Select(preset => preset with { Layout = AddLogPanel(preset.Layout) }).ToList()
        };
    }

    internal static WorkspaceLayoutFile CreateInitial(AppSettings? legacySettings)
    {
        var file = WorkspaceLayoutPresets.CreateDefaultFile();

        if (legacySettings is null)
        {
            return file;
        }

        var sidebarProportion = GetSidebarProportion(legacySettings.SidebarWidth, legacySettings.WindowWidth);
        var (liveProportion, toolProportion) = GetContentProportions(
            legacySettings.WorkspaceLiveColumnWidth,
            legacySettings.WorkspaceToolColumnWidth);
        var root = file.CurrentLayout.Main!;
        var availableProportion = legacySettings.IsSidebarCollapsed ? 1 : 1 - sidebarProportion;
        var live = root.Children[1] with { Proportion = availableProportion * liveProportion };
        var tools = root.Children[2] with { Proportion = availableProportion * toolProportion };

        if (live.Proportion <= 0 || tools.Proportion <= 0)
        {
            live = live with { Proportion = availableProportion * DEFAULT_LIVE_PROPORTION };
            tools = tools with { Proportion = availableProportion * DEFAULT_TOOL_PROPORTION };
        }

        var layout = file.CurrentLayout with
        {
            Main = root with
            {
                Children = legacySettings.IsSidebarCollapsed
                    ? [live, tools]
                    : [root.Children[0] with { Proportion = sidebarProportion }, live, tools]
            },
            HiddenPanelIds = legacySettings.IsSidebarCollapsed ? [WorkbenchPanelIds.WORKSPACE] : []
        };

        return file with { CurrentLayout = layout };
    }

    private static double GetSidebarProportion(double sidebarWidth, double windowWidth)
    {
        if (!IsPositiveFinite(sidebarWidth) || !IsPositiveFinite(windowWidth) || sidebarWidth >= windowWidth)
        {
            return DEFAULT_SIDEBAR_PROPORTION;
        }

        var proportion = sidebarWidth / windowWidth;
        return proportion is > 0 and < 1 ? proportion : DEFAULT_SIDEBAR_PROPORTION;
    }

    private static WorkspaceLayoutSnapshot AddLogPanel(WorkspaceLayoutSnapshot layout)
    {
        if (!WorkspaceLayoutValidator.TryValidate(layout, out var error, legacy: true))
        {
            throw new InvalidDataException(error);
        }

        return layout with { HiddenPanelIds = [.. layout.HiddenPanelIds, WorkbenchPanelIds.LOG] };
    }

    private static (double Live, double Tools) GetContentProportions(double liveWidth, double toolWidth)
    {
        if (!IsPositiveFinite(liveWidth) || !IsPositiveFinite(toolWidth))
        {
            return (DEFAULT_LIVE_PROPORTION, DEFAULT_TOOL_PROPORTION);
        }

        var scale = Math.Max(liveWidth, toolWidth);
        var live = liveWidth / scale;
        var tools = toolWidth / scale;
        var total = live + tools;

        if (live <= 0 || tools <= 0)
        {
            return (DEFAULT_LIVE_PROPORTION, DEFAULT_TOOL_PROPORTION);
        }

        return (live / total, tools / total);
    }

    private static bool IsPositiveFinite(double value)
    {
        return double.IsFinite(value) && value > 0;
    }
}
