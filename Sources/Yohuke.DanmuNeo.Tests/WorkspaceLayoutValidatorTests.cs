using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证布局文件在恢复前拒绝损坏拓扑和不完整面板集合。
/// </summary>
public class WorkspaceLayoutValidatorTests
{
    /// <summary>
    /// 所有面板都隐藏时允许主区域为空。
    /// </summary>
    [Fact]
    public void EmptyMainIsValidWhenEveryPanelIsHidden()
    {
        var layout = new WorkspaceLayoutSnapshot
        {
            HiddenPanelIds = [.. WorkbenchPanelIds.All]
        };

        Assert.True(WorkspaceLayoutValidator.TryValidate(layout, out var error), error);
    }

    /// <summary>
    /// 面板可以全部位于浮窗，窗口坐标可处于主屏幕左侧。
    /// </summary>
    [Fact]
    public void FloatingLayoutPreservesPhysicalPositionAndLogicalSize()
    {
        var layout = new WorkspaceLayoutSnapshot
        {
            FloatingWindows =
            [
                new()
                {
                    Layout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.DEFAULT).Main!,
                    Bounds = new() { X = -1200, Y = 100, Width = 800, Height = 600, Scaling = 2 }
                }
            ],
            FocusedPanelId = WorkbenchPanelIds.COMPOSER
        };

        Assert.True(WorkspaceLayoutValidator.TryValidate(layout, out var error), error);
    }

    /// <summary>
    /// 可见区与隐藏区共同保证每个面板恰好出现一次。
    /// </summary>
    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("hiddenFocus")]
    [InlineData("unknownFocus")]
    public void InvalidPanelMembershipIsRejected(string invalidCase)
    {
        var layout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.TRANSLATION);

        switch (invalidCase)
        {
            case "duplicate":
                layout.HiddenPanelIds.Add(WorkbenchPanelIds.DANMU);
                break;
            case "missing":
                layout.HiddenPanelIds.Remove(WorkbenchPanelIds.LYRICS);
                break;
            case "unknown":
                layout.HiddenPanelIds.Add("unrecognized");
                break;
            case "hiddenFocus":
                layout = layout with { FocusedPanelId = WorkbenchPanelIds.LYRICS };
                break;
            case "unknownFocus":
                layout = layout with { FocusedPanelId = "unrecognized" };
                break;
        }

        AssertInvalid(layout);
    }

    /// <summary>
    /// 标签和分割节点不能混用字段或引用缺失的活动标签。
    /// </summary>
    [Theory]
    [InlineData("tabChildren")]
    [InlineData("emptyTabs")]
    [InlineData("activeOutsideTabs")]
    [InlineData("splitPanels")]
    [InlineData("splitActivePanel")]
    [InlineData("singleSplitChild")]
    [InlineData("unknownKind")]
    [InlineData("unknownOrientation")]
    public void MalformedNodesAreRejected(string invalidCase)
    {
        var layout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.DEFAULT);
        var root = layout.Main!;
        var workspace = root.Children[0];

        switch (invalidCase)
        {
            case "tabChildren":
                workspace.Children.Add(new() { PanelIds = [WorkbenchPanelIds.LYRICS] });
                break;
            case "emptyTabs":
                workspace.PanelIds.Clear();
                break;
            case "activeOutsideTabs":
                root.Children[0] = workspace with { ActivePanelId = WorkbenchPanelIds.DANMU };
                break;
            case "splitPanels":
                root.PanelIds.Add(WorkbenchPanelIds.DANMU);
                break;
            case "splitActivePanel":
                layout = layout with { Main = root with { ActivePanelId = WorkbenchPanelIds.WORKSPACE } };
                break;
            case "singleSplitChild":
                root.Children.RemoveRange(1, 2);
                break;
            case "unknownKind":
                layout = layout with { Main = root with { Kind = (WorkspaceLayoutNodeKind)999 } };
                break;
            case "unknownOrientation":
                layout = layout with { Main = root with { Orientation = (WorkspaceLayoutOrientation)999 } };
                break;
        }

        AssertInvalid(layout);
    }

    /// <summary>
    /// 非有限或非正比例不能进入 Dock 构建流程。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidProportionsAreRejected(double proportion)
    {
        var layout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.DEFAULT);
        AssertInvalid(layout with { Main = layout.Main! with { Proportion = proportion } });
    }

    /// <summary>
    /// 浮窗的无效坐标、大小和缩放在落盘前被拒绝。
    /// </summary>
    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("scaling")]
    public void InvalidFloatingBoundsAreRejected(string field)
    {
        var bounds = new WorkspaceWindowBounds();
        bounds = field switch
        {
            "x" => bounds with { X = double.NaN },
            "y" => bounds with { Y = double.PositiveInfinity },
            "width" => bounds with { Width = 0 },
            "height" => bounds with { Height = -1 },
            _ => bounds with { Scaling = double.NaN }
        };
        AssertInvalid(new()
        {
            FloatingWindows =
            [
                new()
                {
                    Layout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.DEFAULT).Main!,
                    Bounds = bounds
                }
            ]
        });
    }

    /// <summary>
    /// null 集合、null 元素和 null 窗口成员不会导致验证器抛出异常。
    /// </summary>
    [Theory]
    [InlineData("snapshot")]
    [InlineData("floatingList")]
    [InlineData("hiddenList")]
    [InlineData("nodeChildren")]
    [InlineData("nodePanels")]
    [InlineData("child")]
    [InlineData("window")]
    [InlineData("bounds")]
    [InlineData("floatingLayout")]
    public void NullGraphMembersAreRejected(string invalidCase)
    {
        var layout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.DEFAULT);
        var root = layout.Main!;
        layout = invalidCase switch
        {
            "snapshot" => null!,
            "floatingList" => layout with { FloatingWindows = null! },
            "hiddenList" => layout with { HiddenPanelIds = null! },
            "nodeChildren" => layout with { Main = root with { Children = null! } },
            "nodePanels" => layout with { Main = root with { PanelIds = null! } },
            "child" => layout with { Main = root with { Children = [null!, root.Children[1]] } },
            "window" => layout with { FloatingWindows = [null!] },
            "bounds" => layout with { FloatingWindows = [new() { Bounds = null! }] },
            _ => layout with { FloatingWindows = [new() { Layout = null! }] }
        };

        AssertInvalid(layout);
    }

    /// <summary>
    /// 直接或间接环引用被识别，不会递归耗尽栈。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyclicGraphsAreRejected(bool indirect)
    {
        var root = new WorkspaceLayoutNode { Kind = WorkspaceLayoutNodeKind.Split };
        var child = new WorkspaceLayoutNode { Kind = WorkspaceLayoutNodeKind.Split };
        root.Children.Add(indirect ? child : root);
        root.Children.Add(new() { PanelIds = [WorkbenchPanelIds.WORKSPACE] });
        child.Children.Add(root);
        child.Children.Add(new() { PanelIds = [WorkbenchPanelIds.DANMU] });

        AssertInvalid(new() { Main = root });
    }

    /// <summary>
    /// 过深节点链被拒绝，而不是进入无限的布局恢复过程。
    /// </summary>
    [Fact]
    public void ExcessiveDepthIsRejected()
    {
        var node = new WorkspaceLayoutNode { PanelIds = [WorkbenchPanelIds.COMPOSER] };

        for (var index = 0; index < 40; index++)
        {
            node = new()
            {
                Kind = WorkspaceLayoutNodeKind.Split,
                Children = [node, new() { PanelIds = [WorkbenchPanelIds.WORKSPACE] }]
            };
        }

        AssertInvalid(new() { Main = node });
    }

    /// <summary>
    /// 自定义预设可以作为当前布局，但必须同时提供有效定义。
    /// </summary>
    [Fact]
    public void UserPresetCanBeSelectedWithAValidDefinition()
    {
        var file = WorkspaceLayoutPresets.CreateDefaultFile() with
        {
            CurrentPresetId = "user-selected",
            UserPresets =
            [
                new()
                {
                    Id = "user-selected", Name = "我的直播布局",
                    Layout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.LYRICS)
                }
            ]
        };

        Assert.True(WorkspaceLayoutValidator.TryValidate(file, out var error), error);
    }

    /// <summary>
    /// 自定义布局不能使用已在菜单中显示的内置布局名称。
    /// </summary>
    [Theory]
    [InlineData("默认")]
    [InlineData("同传")]
    [InlineData("歌词")]
    [InlineData("转发")]
    public void UserPresetCannotReuseABuiltInName(string name)
    {
        var file = WorkspaceLayoutPresets.CreateDefaultFile() with
        {
            UserPresets =
            [
                new()
                {
                    Id = "user-preset", Name = name,
                    Layout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.DEFAULT)
                }
            ]
        };

        Assert.False(WorkspaceLayoutValidator.TryValidate(file, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    /// 文件版本、预设选择与用户预设标识和名称保持完整一致。
    /// </summary>
    [Theory]
    [InlineData("version")]
    [InlineData("selection")]
    [InlineData("currentNull")]
    [InlineData("presetListNull")]
    [InlineData("presetNull")]
    [InlineData("presetLayoutNull")]
    [InlineData("reservedId")]
    [InlineData("emptyId")]
    [InlineData("duplicateId")]
    [InlineData("emptyName")]
    [InlineData("controlName")]
    [InlineData("duplicateName")]
    public void InvalidLayoutFilesAreRejected(string invalidCase)
    {
        var preset = new WorkspaceLayoutPreset
        {
            Id = "user-one", Name = "自定义布局",
            Layout = WorkspaceLayoutPresets.Create(WorkspaceLayoutPresets.DEFAULT)
        };
        var file = WorkspaceLayoutPresets.CreateDefaultFile() with { UserPresets = [preset] };
        file = invalidCase switch
        {
            "version" => file with { Version = WorkspaceLayoutFile.CURRENT_VERSION + 1 },
            "selection" => file with { CurrentPresetId = "missing" },
            "currentNull" => file with { CurrentLayout = null! },
            "presetListNull" => file with { UserPresets = null! },
            "presetNull" => file with { UserPresets = [null!] },
            "presetLayoutNull" => file with { UserPresets = [preset with { Layout = null! }] },
            "reservedId" => file with { UserPresets = [preset with { Id = WorkspaceLayoutPresets.DEFAULT }] },
            "emptyId" => file with { UserPresets = [preset with { Id = " " }] },
            "duplicateId" => file with { UserPresets = [preset, preset with { Name = "另一个布局" }] },
            "emptyName" => file with { UserPresets = [preset with { Name = " " }] },
            "controlName" => file with { UserPresets = [preset with { Name = "布局\n名称" }] },
            _ => file with
            {
                UserPresets = [preset with { Name = "Live" }, preset with { Id = "user-two", Name = "LIVE" }]
            }
        };

        Assert.False(WorkspaceLayoutValidator.TryValidate(file, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private static void AssertInvalid(WorkspaceLayoutSnapshot layout)
    {
        Assert.False(WorkspaceLayoutValidator.TryValidate(layout, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
