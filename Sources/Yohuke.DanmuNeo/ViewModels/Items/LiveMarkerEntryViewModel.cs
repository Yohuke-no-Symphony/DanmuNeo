using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>具有行级编辑事务的单条直播记录。</summary>
public sealed class LiveMarkerEntryViewModel : ObservableObject, IEditableObject, INotifyDataErrorInfo
{
    private readonly Action<LiveMarkerEntryViewModel, LiveMarkerEntry, LiveMarkerEntry> commit;
    private LiveMarkerEntry committed;
    private string liveTimeText;
    private string message;
    private LiveMarkerColor markerColor;
    private bool isEditing;

    /// <summary>连接一条不可变记录及其提交入口。</summary>
    public LiveMarkerEntryViewModel(LiveMarkerEntry entry, IReadOnlyList<LiveMarkerColorOption> colorOptions,
        Action<LiveMarkerEntryViewModel, LiveMarkerEntry, LiveMarkerEntry> commit)
    {
        committed = entry;
        this.commit = commit;
        ColorOptions = colorOptions;
        liveTimeText = LiveMarkerTimeInput.Format(entry.LiveTime);
        message = entry.Message;
        markerColor = entry.MarkerColor;
    }

    /// <summary>已提交的存储值。</summary>
    public LiveMarkerEntry Snapshot => committed;

    /// <summary>独立捕获且只读的实际时间。</summary>
    public string RealTimeText => committed.RealDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);

    /// <summary>可编辑的累计直播时间。</summary>
    public string LiveTimeText
    {
        get => liveTimeText;
        set
        {
            if (SetProperty(ref liveTimeText, value))
            {
                OnPropertyChanged(nameof(HasErrors));
                ErrorsChanged?.Invoke(this, new(nameof(LiveTimeText)));
            }
        }
    }

    /// <summary>记录备注。</summary>
    public string Message
    {
        get => message;
        set => SetProperty(ref message, value);
    }

    /// <summary>记录颜色。</summary>
    public LiveMarkerColor MarkerColor
    {
        get => markerColor;
        set
        {
            if (SetProperty(ref markerColor, value))
            {
                OnPropertyChanged(nameof(ColorName));
                OnPropertyChanged(nameof(ColorBrush));
            }
        }
    }

    /// <summary>颜色选择项。</summary>
    public IReadOnlyList<LiveMarkerColorOption> ColorOptions { get; }

    /// <summary>当前颜色名称。</summary>
    public string ColorName => ColorOptions.First(option => option.Value == MarkerColor).Name;

    /// <summary>表格内的颜色指示。</summary>
    public IBrush ColorBrush => ColorOptions.First(option => option.Value == MarkerColor).Brush;

    /// <inheritdoc/>
    public bool HasErrors => !LiveMarkerTimeInput.TryParse(LiveTimeText, out _);

    /// <inheritdoc/>
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <inheritdoc/>
    public IEnumerable GetErrors(string? propertyName)
    {
        return HasErrors && propertyName is null or nameof(LiveTimeText)
            ? new[] { Localization.Get("LiveMarker.InvalidTime") }
            : Array.Empty<string>();
    }

    /// <inheritdoc/>
    public void BeginEdit()
    {
        isEditing = true;
    }

    /// <inheritdoc/>
    public void EndEdit()
    {
        if (!isEditing || !LiveMarkerTimeInput.TryParse(LiveTimeText, out var time))
        {
            return;
        }

        isEditing = false;
        var before = committed;
        if (LiveTimeText == LiveMarkerTimeInput.Format(committed.LiveTime))
        {
            time = committed.LiveTime;
        }

        var after = committed with { LiveTime = time, Message = Message, MarkerColor = MarkerColor };
        committed = after;
        commit(this, before, after);
    }

    /// <inheritdoc/>
    public void CancelEdit()
    {
        isEditing = false;
        LiveTimeText = LiveMarkerTimeInput.Format(committed.LiveTime);
        Message = committed.Message;
        MarkerColor = committed.MarkerColor;
    }

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(ColorName));
    }
}
