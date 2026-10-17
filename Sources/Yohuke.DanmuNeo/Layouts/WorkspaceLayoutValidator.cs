using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Layouts;

internal static class WorkspaceLayoutValidator
{
    internal const int MAXIMUM_DEPTH = 32;

    internal static bool TryValidate(WorkspaceLayoutSnapshot layout, out string error, bool legacy = false)
    {
        return TryValidate(layout, out error, out _, legacy);
    }

    internal static bool TryValidate(WorkspaceLayoutSnapshot layout, out string error, out string errorKey,
        bool legacy = false)
    {
        var valid = TryValidateSnapshot(layout, out errorKey, legacy);
        error = errorKey.Length == 0 ? string.Empty : Localization.Get(errorKey);
        return valid;
    }

    internal static bool TryValidate(WorkspaceLayoutFile file, out string error)
    {
        return TryValidate(file, out error, out _);
    }

    internal static bool TryValidate(WorkspaceLayoutFile file, out string error, out string errorKey)
    {
        var valid = TryValidateFile(file, out errorKey);
        error = errorKey.Length == 0 ? string.Empty : Localization.Get(errorKey);
        return valid;
    }

    private static bool TryValidateSnapshot(WorkspaceLayoutSnapshot layout, out string error, bool legacy)
    {
        error = "";

        if (layout is null || layout.FloatingWindows is null || layout.HiddenPanelIds is null)
        {
            error = "Layout.Validation.CollectionsRequired";
            return false;
        }

        var panelIds = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new HashSet<WorkspaceLayoutNode>(ReferenceEqualityComparer.Instance);

        if (layout.Main is not null && !TryValidateNode(layout.Main, panelIds, nodes, 0, out error))
        {
            return false;
        }

        foreach (var floating in layout.FloatingWindows)
        {
            if (floating is null || floating.Bounds is null || !IsValidBounds(floating.Bounds))
            {
                error = "Layout.Validation.InvalidBounds";
                return false;
            }

            if (!TryValidateNode(floating.Layout, panelIds, nodes, 0, out error))
            {
                return false;
            }
        }

        if (layout.FocusedPanelId is { } focused && !panelIds.Contains(focused))
        {
            error = "Layout.Validation.InvalidFocus";
            return false;
        }

        foreach (var panelId in layout.HiddenPanelIds)
        {
            if (!TryAddPanel(panelId, panelIds, out error))
            {
                return false;
            }
        }

        var expected = legacy
            ? WorkbenchPanelIds.All.Where(id => id != WorkbenchPanelIds.LOG)
            : WorkbenchPanelIds.All;

        if (!panelIds.SetEquals(expected))
        {
            error = "Layout.Validation.IncompletePanels";
            return false;
        }

        return true;
    }

    private static bool TryValidateFile(WorkspaceLayoutFile file, out string error)
    {
        error = "";

        if (file is null || file.Version != WorkspaceLayoutFile.CURRENT_VERSION)
        {
            error = "Layout.Validation.UnsupportedVersion";
            return false;
        }

        if (file.UserPresets is null)
        {
            error = "Layout.Validation.UserPresetsRequired";
            return false;
        }

        if (!TryValidateSnapshot(file.CurrentLayout, out error, false))
        {
            return false;
        }

        var presetIds = new HashSet<string>(StringComparer.Ordinal)
        {
            WorkspaceLayoutPresets.DEFAULT, WorkspaceLayoutPresets.TRANSLATION,
            WorkspaceLayoutPresets.LYRICS, WorkspaceLayoutPresets.FORWARDING
        };
        var presetNames = new HashSet<string>(
            WorkspaceLayoutPresets.BuiltIn.Select(preset => preset.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var preset in file.UserPresets)
        {
            if (preset is null || !IsValidText(preset.Id) || !presetIds.Add(preset.Id))
            {
                error = "Layout.Validation.InvalidPresetId";
                return false;
            }

            if (!IsValidText(preset.Name) || !presetNames.Add(preset.Name.Trim()))
            {
                error = "Layout.Validation.InvalidPresetName";
                return false;
            }

            if (!TryValidateSnapshot(preset.Layout, out error, false))
            {
                return false;
            }
        }

        if (file.CurrentPresetId is null || !presetIds.Contains(file.CurrentPresetId))
        {
            error = "Layout.Validation.CurrentPresetMissing";
            return false;
        }

        return true;
    }

    private static bool TryValidateNode(
        WorkspaceLayoutNode node,
        HashSet<string> panelIds,
        HashSet<WorkspaceLayoutNode> nodes,
        int depth,
        out string error)
    {
        error = "";

        if (node is null || node.Children is null || node.PanelIds is null)
        {
            error = "Layout.Validation.NodeContentRequired";
            return false;
        }

        if (depth > MAXIMUM_DEPTH || !nodes.Add(node))
        {
            error = "Layout.Validation.InvalidNodeGraph";
            return false;
        }

        if (!double.IsFinite(node.Proportion) || node.Proportion <= 0 ||
            !Enum.IsDefined(node.Orientation))
        {
            error = "Layout.Validation.InvalidNodeGeometry";
            return false;
        }

        switch (node.Kind)
        {
            case WorkspaceLayoutNodeKind.Tabs:
                if (node.Children.Count != 0 || node.PanelIds.Count == 0 ||
                    node.ActivePanelId is { } active && !node.PanelIds.Contains(active, StringComparer.Ordinal))
                {
                    error = "Layout.Validation.InvalidTabGroup";
                    return false;
                }

                foreach (var panelId in node.PanelIds)
                {
                    if (!TryAddPanel(panelId, panelIds, out error))
                    {
                        return false;
                    }
                }

                return true;
            case WorkspaceLayoutNodeKind.Split:
                if (node.Children.Count < 2 || node.PanelIds.Count != 0 || node.ActivePanelId is not null)
                {
                    error = "Layout.Validation.InvalidSplit";
                    return false;
                }

                foreach (var child in node.Children)
                {
                    if (!TryValidateNode(child, panelIds, nodes, depth + 1, out error))
                    {
                        return false;
                    }
                }

                return true;
            default:
                error = "Layout.Validation.UnknownNode";
                return false;
        }
    }

    private static bool TryAddPanel(string panelId, HashSet<string> panelIds, out string error)
    {
        if (panelId is null || !WorkbenchPanelIds.All.Contains(panelId, StringComparer.Ordinal) || !panelIds.Add(panelId))
        {
            error = "Layout.Validation.InvalidPanel";
            return false;
        }

        error = "";
        return true;
    }

    private static bool IsValidBounds(WorkspaceWindowBounds bounds)
    {
        return double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) &&
            double.IsFinite(bounds.Width) && bounds.Width > 0 &&
            double.IsFinite(bounds.Height) && bounds.Height > 0 &&
            double.IsFinite(bounds.Scaling) && bounds.Scaling > 0;
    }

    private static bool IsValidText(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value == value.Trim() && !value.Any(char.IsControl);
    }
}
