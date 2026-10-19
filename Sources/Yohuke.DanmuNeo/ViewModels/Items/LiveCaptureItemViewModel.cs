using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using Yohuke.DanmuNeo.Models.Captures;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>单张截图的纯展示状态；图像解码与平台对象由 View 持有。</summary>
public sealed class LiveCaptureItemViewModel : ObservableObject
{
    private readonly LiveCaptureRecord record;
    private readonly Action<string, string> updateNote;
    private string note;
    private string? previewPath;

    /// <summary>连接独立元数据和可重试备注保存入口。</summary>
    public LiveCaptureItemViewModel(LiveCaptureRecord record, Action<string, string> updateNote)
    {
        this.record = record;
        this.updateNote = updateNote;
        note = record.Note;
    }

    public string Id => record.Id;
    public string RoomId => record.RoomId;
    public string BroadcastSessionId => record.BroadcastSessionId;
    public DateTimeOffset CapturedAt => record.CapturedAt;
    public DateTimeOffset? StartedAt => record.StartedAt;
    public string Title => string.IsNullOrWhiteSpace(record.RoomName) ? record.RoomId : $"{record.RoomName} ({record.RoomId})";
    public string StartedAtText => LiveCaptureTimeFormatter.FormatTime(record.StartedAt);
    public string ElapsedText => LiveCaptureTimeFormatter.FormatElapsed(record.StartedAt, record.CapturedAt);
    public string CapturedAtText => LiveCaptureTimeFormatter.FormatTime(record.CapturedAt);
    /// <summary>列表中紧随房间名称的抓图时刻与开播后时长。</summary>
    public string ListTimeText => $"{CapturedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture)} · {ElapsedText}";
    /// <summary>只有实际备注内容才占用列表备注行。</summary>
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);
    public LiveCaptureRecord Snapshot => record with { Note = note };

    public string Note
    {
        get => note;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, 10000);
            if (note == value)
            {
                return;
            }
            updateNote(Id, value);
            SetProperty(ref note, value);
            OnPropertyChanged(nameof(HasNote));
        }
    }

    public string? PreviewPath
    {
        get => previewPath;
        private set => SetProperty(ref previewPath, value);
    }

    /// <summary>仅在原图落盘后交给 View 解码，重试成功时可恢复预览。</summary>
    public void RefreshPreview(string path)
    {
        PreviewPath = File.Exists(path) ? path : null;
    }

    /// <summary>刷新依赖当前语言的展示字段。</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(StartedAtText));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(CapturedAtText));
        OnPropertyChanged(nameof(ListTimeText));
    }
}
