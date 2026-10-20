using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.LiveMarker;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>记录颜色的本地化选项。</summary>
public sealed class LiveMarkerColorOption : ObservableObject
{
    /// <summary>创建颜色选项。</summary>
    public LiveMarkerColorOption(LiveMarkerColor value)
    {
        Value = value;
        Brush = new SolidColorBrush(Color.Parse(value switch
        {
            LiveMarkerColor.Red => "#EF5350",
            LiveMarkerColor.Orange => "#FF9800",
            LiveMarkerColor.Yellow => "#C6A700",
            LiveMarkerColor.Green => "#4CAF50",
            LiveMarkerColor.Blue => "#42A5F5",
            LiveMarkerColor.Magenta => "#AB47BC",
            _ => "#9E9E9E"
        }));
    }

    /// <summary>持久化颜色值。</summary>
    public LiveMarkerColor Value { get; }

    /// <summary>本地化颜色名称。</summary>
    public string Name => Localization.Get($"LiveMarker.Color.{Value}");

    /// <summary>颜色预览。</summary>
    public IBrush Brush { get; }

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
    }
}
