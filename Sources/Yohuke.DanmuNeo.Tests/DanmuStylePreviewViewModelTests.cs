using Avalonia.Media;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证 DEBUG 预览数据覆盖正式弹幕、粉丝牌、SC 和上舰展示所需的样式场景。
/// </summary>
public class DanmuStylePreviewViewModelTests
{
    /// <summary>
    /// 紧凑模式切换通知视图刷新，以便同一组样本验证两种布局。
    /// </summary>
    [Fact]
    public void CompactDanmuDisplayRaisesNotification()
    {
        var viewModel = new DanmuStylePreviewViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        viewModel.IsCompactDanmuDisplay = true;

        Assert.True(viewModel.IsCompactDanmuDisplay);
        Assert.Contains(nameof(DanmuStylePreviewViewModel.IsCompactDanmuDisplay), changedProperties);
    }

    /// <summary>
    /// 默认样本同时包含六档 SC、三档上舰以及有牌、无牌普通用户。
    /// </summary>
    [Fact]
    public void ConstructorCreatesAllPaidTiersAndDanmuVariants()
    {
        var viewModel = new DanmuStylePreviewViewModel();

        Assert.Equal(6, viewModel.SuperChatPresets.Count);
        var superChats = viewModel.SuperChats.Where(item => item.Kind == LivePaidEventKind.SuperChat).ToArray();
        var guardPurchases = viewModel.SuperChats.Where(item => item.Kind == LivePaidEventKind.GuardPurchase).ToArray();
        Assert.Equal(6, superChats.Length);
        Assert.Equal(3, guardPurchases.Length);
        Assert.Equal(8, viewModel.DanmuItems.Count);
        Assert.Equal("300 电池", superChats[0].BadgeText);
        Assert.Equal(Color.Parse("#3171D2"), superChats[0].BorderColor);
        Assert.Equal("20000 电池", superChats[^1].BadgeText);
        Assert.Equal(new[] { "总督", "提督", "舰长" }, guardPurchases.Select(item => item.BadgeText));
        Assert.Equal(3, guardPurchases.Select(item => item.BorderColor).Distinct().Count());
        Assert.All(guardPurchases, item =>
        {
            Assert.Contains(item.GuardLevelName, item.UserName);
            Assert.False(item.CanInsertContent);
            Assert.Empty(item.Content);
        });
        Assert.Contains(viewModel.DanmuItems, item => item.HasMedal && item.MedalLevel > 0);
        Assert.Contains(viewModel.DanmuItems, item => !item.HasMedal);
        Assert.Equal(new[] { 0, 1, 2, 3 }, viewModel.DanmuItems.Where(item => item.HasMedal)
            .Select(item => item.MedalGuardLevel).Distinct().Order());
    }

    /// <summary>
    /// 粉丝牌身份可以独立于上舰记录选择，并用于单条和批量弹幕。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EditableMedalGuardTierAppliesToSingleAndBatchDanmu(int level)
    {
        var viewModel = new DanmuStylePreviewViewModel();
        viewModel.SelectedMedalGuardPreset = viewModel.MedalGuardPresets.Single(preset => preset.GuardLevel == level);
        viewModel.SelectedGuardPreset = viewModel.GuardPresets.First(preset => preset.GuardLevel != level);
        viewModel.ClearSamples();

        viewModel.AddDanmu();
        viewModel.AddDanmuBatch();

        Assert.All(viewModel.DanmuItems, item => Assert.Equal(level, item.MedalGuardLevel));
        Assert.Equal(level, viewModel.SelectedMedalGuardPreset.GuardLevel);
    }

    /// <summary>
    /// 编辑字段经过去空白和缺省处理后生成弹幕，并携带粉丝牌名称与等级。
    /// </summary>
    [Fact]
    public void AddDanmuUsesEditableFieldsAndFallbacks()
    {
        var viewModel = new DanmuStylePreviewViewModel
        {
            UserName = "  ",
            Content = "  ",
            DanmuStatus = " 已转发 ",
            MedalName = " 肉肉 ",
            MedalLevel = 25
        };

        viewModel.AddDanmu();

        var item = viewModel.DanmuItems[0];
        Assert.Equal("测试用户", item.UserName);
        Assert.Equal("空内容样式测试", item.Content);
        Assert.Equal("已转发", item.Status);
        Assert.Equal("肉肉 25", item.MedalText);
    }

    /// <summary>
    /// 空牌名或非正等级不会生成可见徽章，编辑器中的负等级被限制为零。
    /// </summary>
    [Theory]
    [InlineData(" ", 25)]
    [InlineData("肉肉", 0)]
    [InlineData("肉肉", -1)]
    public void AddDanmuSupportsUsersWithoutMedals(string medalName, int medalLevel)
    {
        var viewModel = new DanmuStylePreviewViewModel
        {
            MedalName = medalName,
            MedalLevel = medalLevel
        };

        viewModel.AddDanmu();

        Assert.False(viewModel.DanmuItems[0].HasMedal);
        Assert.Empty(viewModel.DanmuItems[0].MedalText);
        Assert.True(viewModel.MedalLevel >= 0);
    }

    /// <summary>
    /// 批量样本保留既有记录，并生成带可识别序号和当前编辑字段的足量弹幕用于翻阅。
    /// </summary>
    [Fact]
    public void AddDanmuBatchPreservesExistingRowsAndEditableMetadata()
    {
        var viewModel = new DanmuStylePreviewViewModel
        {
            UserName = " 批量用户 ",
            Content = " 批量正文 ",
            MedalName = " 肉肉 ",
            MedalLevel = 12,
            DanmuStatus = " 已转发 "
        };
        var existingItems = viewModel.DanmuItems.ToArray();

        viewModel.AddDanmuBatch();

        var newItems = viewModel.DanmuItems.Take(viewModel.DanmuItems.Count - existingItems.Length).ToArray();
        Assert.True(newItems.Length >= 50);
        Assert.Equal(newItems.Length, newItems.Select(item => item.Content).Distinct().Count());
        Assert.All(newItems, item =>
        {
            Assert.Equal("批量用户", item.UserName);
            Assert.StartsWith("批量正文", item.Content);
            Assert.Equal("肉肉 12", item.MedalText);
            Assert.Equal("已转发", item.Status);
        });
        Assert.Equal(existingItems, viewModel.DanmuItems.Skip(newItems.Length));
        Assert.True(newItems[0].Time >= newItems[^1].Time);
    }

    /// <summary>
    /// SC 预览按选定档位显示电池金额，并接受手动输入的边框颜色。
    /// </summary>
    [Fact]
    public void AddSuperChatUsesBatteryAmountAndCustomColor()
    {
        var viewModel = new DanmuStylePreviewViewModel
        {
            SelectedSuperChatPreset = null
        };
        viewModel.SelectedSuperChatPreset = viewModel.SuperChatPresets[2];
        viewModel.SuperChatColorText = "#123456";

        viewModel.AddSuperChat();

        var item = viewModel.SuperChats[0];
        Assert.Equal("1000 电池", item.PriceText);
        Assert.Equal(Color.Parse("#123456"), item.BorderColor);
        Assert.Equal(LivePaidEventKind.SuperChat, item.Kind);
    }

    /// <summary>
    /// 三档上舰均使用正式事件类型、用户昵称和档位徽章，避免显示虚构的 SC 金额。
    /// </summary>
    [Theory]
    [InlineData(0, "总督", "#E89539")]
    [InlineData(1, "提督", "#B45AB8")]
    [InlineData(2, "舰长", "#3171D2")]
    public void AddGuardPurchaseUsesSelectedTierAndUserName(int presetIndex, string levelName, string color)
    {
        var viewModel = new DanmuStylePreviewViewModel
        {
            UserName = " 上舰用户 "
        };
        viewModel.SelectedGuardPreset = viewModel.GuardPresets[presetIndex];

        viewModel.AddGuardPurchase();

        var item = viewModel.SuperChats[0];
        Assert.Equal(LivePaidEventKind.GuardPurchase, item.Kind);
        Assert.Equal("上舰用户", item.UserName);
        Assert.Equal(levelName, item.GuardLevelName);
        Assert.Equal(levelName, item.BadgeText);
        Assert.Equal(Color.Parse(color), item.BorderColor);
        Assert.Empty(item.PriceText);
        Assert.Empty(item.Content);
        Assert.False(item.CanInsertContent);
    }

    /// <summary>
    /// 清空档位选择时不会添加无效上舰记录。
    /// </summary>
    [Fact]
    public void AddGuardPurchaseWithoutSelectedTierLeavesFeedUnchanged()
    {
        var viewModel = new DanmuStylePreviewViewModel
        {
            SelectedGuardPreset = null
        };
        var existingItems = viewModel.SuperChats.ToArray();

        viewModel.AddGuardPurchase();

        Assert.Equal(existingItems, viewModel.SuperChats);
    }

    /// <summary>
    /// 恢复样本保留绑定集合实例，使滚动订阅有效，同时移除临时批量数据并恢复三档上舰。
    /// </summary>
    [Fact]
    public void ResetSamplesRestoresDefaultsWithoutReplacingFeedCollections()
    {
        var viewModel = new DanmuStylePreviewViewModel();
        var danmuItems = viewModel.DanmuItems;
        var superChats = viewModel.SuperChats;
        viewModel.AddDanmuBatch();
        viewModel.AddGuardPurchase();

        viewModel.ResetSamples();

        Assert.Same(danmuItems, viewModel.DanmuItems);
        Assert.Same(superChats, viewModel.SuperChats);
        Assert.DoesNotContain(viewModel.DanmuItems, item => item.Content.Contains("（批量"));
        Assert.Equal(3, viewModel.SuperChats.Count(item => item.Kind == LivePaidEventKind.GuardPurchase));
        Assert.Contains(viewModel.DanmuItems, item => item.HasMedal);
        Assert.Contains(viewModel.DanmuItems, item => !item.HasMedal);
    }

    /// <summary>
    /// 清空操作同时移除普通弹幕和 SC、上舰混合列表中的所有样本。
    /// </summary>
    [Fact]
    public void ClearSamplesClearsBothFeeds()
    {
        var viewModel = new DanmuStylePreviewViewModel();

        viewModel.ClearSamples();

        Assert.Empty(viewModel.SuperChats);
        Assert.Empty(viewModel.DanmuItems);
    }
}
