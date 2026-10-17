namespace Yohuke.DanmuNeo.ViewModels.Items;

internal sealed record LayoutPresetRow(string Id, string Name, bool IsReadOnly)
{
    public string DisplayName => IsReadOnly ? $"{Name}（内置）" : Name;
}
