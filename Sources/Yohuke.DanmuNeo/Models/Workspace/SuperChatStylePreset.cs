using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Models.Workspace;

internal sealed class SuperChatStylePreset : ObservableObject
{
    public decimal CnyPrice { get; init; }

    public int BatteryAmount { get; init; }

    public string ColorNameKey { get; init; } = "";

    public string ColorName => Localization.Get(ColorNameKey);

    public Color BorderColor { get; init; }

    public string PriceText => Localization.Format("Preview.BatteryAmount", BatteryAmount);

    public string DisplayName => $"{PriceText} · ￥{CnyPrice:0.##} · {ColorName}";

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ColorName));
        OnPropertyChanged(nameof(PriceText));
        OnPropertyChanged(nameof(DisplayName));
    }
}
