namespace Yohuke.DanmuNeo.Layouts;

internal static class WorkspaceLayoutValidator
{
    internal const int MAXIMUM_DEPTH = 32;

    internal static bool TryValidate(WorkspaceLayoutSnapshot layout, out string error)
    {
        error = "";

        if (layout is null || layout.FloatingWindows is null || layout.HiddenPanelIds is null)
        {
            error = "布局缺少必要的面板或窗口集合。";
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
                error = "浮动窗口的位置、大小或缩放无效。";
                return false;
            }

            if (!TryValidateNode(floating.Layout, panelIds, nodes, 0, out error))
            {
                return false;
            }
        }

        if (layout.FocusedPanelId is { } focused && !panelIds.Contains(focused))
        {
            error = "聚焦面板不在当前可见布局中。";
            return false;
        }

        foreach (var panelId in layout.HiddenPanelIds)
        {
            if (!TryAddPanel(panelId, panelIds, out error))
            {
                return false;
            }
        }

        if (!panelIds.SetEquals(WorkbenchPanelIds.All))
        {
            error = "布局必须恰好包含全部八个工作台面板。";
            return false;
        }

        return true;
    }

    internal static bool TryValidate(WorkspaceLayoutFile file, out string error)
    {
        error = "";

        if (file is null || file.Version != WorkspaceLayoutFile.CURRENT_VERSION)
        {
            error = "布局文件版本不受支持。";
            return false;
        }

        if (file.UserPresets is null)
        {
            error = "布局文件缺少自定义布局集合。";
            return false;
        }

        if (!TryValidate(file.CurrentLayout, out error))
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
                error = "自定义布局的标识为空、重复或与内置布局冲突。";
                return false;
            }

            if (!IsValidText(preset.Name) || !presetNames.Add(preset.Name.Trim()))
            {
                error = "自定义布局名称为空、含控制字符或与其他名称重复。";
                return false;
            }

            if (!TryValidate(preset.Layout, out error))
            {
                return false;
            }
        }

        if (file.CurrentPresetId is null || !presetIds.Contains(file.CurrentPresetId))
        {
            error = "当前选中的布局不存在。";
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
            error = "布局节点缺少必要内容。";
            return false;
        }

        if (depth > MAXIMUM_DEPTH || !nodes.Add(node))
        {
            error = "布局节点过深或存在重复、循环引用。";
            return false;
        }

        if (!double.IsFinite(node.Proportion) || node.Proportion <= 0 ||
            !Enum.IsDefined(node.Orientation))
        {
            error = "布局节点的比例或方向无效。";
            return false;
        }

        switch (node.Kind)
        {
            case WorkspaceLayoutNodeKind.Tabs:
                if (node.Children.Count != 0 || node.PanelIds.Count == 0 ||
                    node.ActivePanelId is { } active && !node.PanelIds.Contains(active, StringComparer.Ordinal))
                {
                    error = "标签组必须包含面板，活动标签必须属于该组。";
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
                    error = "分割节点必须包含至少两个子区域，不能直接包含标签。";
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
                error = "布局包含未知节点类型。";
                return false;
        }
    }

    private static bool TryAddPanel(string panelId, HashSet<string> panelIds, out string error)
    {
        if (panelId is null || !WorkbenchPanelIds.All.Contains(panelId, StringComparer.Ordinal) || !panelIds.Add(panelId))
        {
            error = "布局包含未知或重复面板。";
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
