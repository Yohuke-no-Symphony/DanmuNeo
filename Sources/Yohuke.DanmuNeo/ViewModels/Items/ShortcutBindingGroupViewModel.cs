using CommunityToolkit.Mvvm.ComponentModel;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>
/// 快捷键绑定分组视图模型。
/// </summary>
public class ShortcutBindingGroupViewModel : ObservableObject
{
    private readonly string name;

    /// <summary>
    /// 初始化快捷键绑定分组视图模型。
    /// </summary>
    public ShortcutBindingGroupViewModel(string name, IReadOnlyList<ShortcutBindingViewModel> bindings)
    {
        this.name = name;
        Bindings = bindings;
    }

    /// <summary>
    /// 分组名称。
    /// </summary>
    public string Name => Bindings.FirstOrDefault()?.Definition.GroupName ?? name;

    /// <summary>
    /// 快捷键绑定。
    /// </summary>
    public IReadOnlyList<ShortcutBindingViewModel> Bindings { get; }

    /// <summary>
    /// 刷新分组与现有快捷键行的显示文案。
    /// </summary>
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));

        foreach (var binding in Bindings)
        {
            binding.RefreshLocalization();
        }
    }
}
