using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Layouts;

internal static class WorkspaceLayoutMigration
{
    private const double DEFAULT_SIDEBAR_PROPORTION = 0.2;
    private const double DEFAULT_LIVE_PROPORTION = 0.625;
    private const double DEFAULT_TOOL_PROPORTION = 0.375;

    internal static WorkspaceLayoutFile Upgrade(WorkspaceLayoutFile file)
    {
        if (file.Version < 1 || file.Version >= WorkspaceLayoutFile.CURRENT_VERSION)
        {
            return file;
        }

        if (file.UserPresets is null || file.UserPresets.Any(preset => preset is null))
        {
            throw LocalizedExceptionText.Create("Layout.Validation.ValidUserPresetsRequired",
                message => new InvalidDataException(message));
        }

        while (file.Version < WorkspaceLayoutFile.CURRENT_VERSION)
        {
            var sourceVersion = file.Version;
            var additions = WorkbenchPanelIds.ForVersion(sourceVersion + 1)
                .Except(WorkbenchPanelIds.ForVersion(sourceVersion), StringComparer.Ordinal).ToArray();
            file = file with
            {
                Version = sourceVersion + 1,
                CurrentLayout = AddHiddenPanels(file.CurrentLayout, sourceVersion, additions),
                UserPresets = file.UserPresets.Select(preset => preset with
                {
                    Layout = AddHiddenPanels(preset.Layout, sourceVersion, additions)
                }).ToList()
            };
        }

        return file;
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
            HiddenPanelIds = legacySettings.IsSidebarCollapsed
                ? [.. file.CurrentLayout.HiddenPanelIds, WorkbenchPanelIds.WORKSPACE]
                : [.. file.CurrentLayout.HiddenPanelIds]
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

    private static WorkspaceLayoutSnapshot AddHiddenPanels(WorkspaceLayoutSnapshot layout, int sourceVersion,
        IReadOnlyList<string> additions)
    {
        if (!WorkspaceLayoutValidator.TryValidate(layout, out _, out var errorKey, sourceVersion))
        {
            throw LocalizedExceptionText.Create(errorKey, message => new InvalidDataException(message));
        }

        return layout with { HiddenPanelIds = [.. layout.HiddenPanelIds, .. additions] };
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
