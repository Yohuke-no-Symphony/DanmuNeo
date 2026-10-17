using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.ViewModels.Items;

internal sealed class LayoutPresetRow(string id, string name, bool isReadOnly) : ObservableObject
{
    public string Id { get; } = id;
    public bool IsReadOnly { get; } = isReadOnly;
    public string Name => IsReadOnly ? WorkspaceLayoutPresets.GetDisplayName(Id, name) : name;
    public string DisplayName => IsReadOnly ? Localization.Format("Layout.BuiltInDisplayName", Name) : Name;

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayName));
    }
}
