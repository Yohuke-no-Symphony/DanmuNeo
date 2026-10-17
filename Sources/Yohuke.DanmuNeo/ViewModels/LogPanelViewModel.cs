using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Logging;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels;

/// <summary>
/// 筛选应用日志并在 UI 线程合并后台通知。
/// </summary>
public sealed class LogPanelViewModel : ViewModelBase, IDisposable
{
    private readonly IAppLogService log;
    private readonly ObservableCollection<string> levels = [string.Empty, "INFO", "WARNING", "ERROR"];
    private int filterIndex;
    private string filterText = string.Empty;
    private AppLogEntry? selectedEntry;
    private Func<bool>? readingState;
    private int refreshPending;
    private volatile bool disposed;
    private bool refreshingLevels;

    /// <summary>连接应用会话日志，不接管日志服务的生命周期。</summary>
    public LogPanelViewModel(IAppLogService log)
    {
        ArgumentNullException.ThrowIfNull(log);
        this.log = log;
        levels[0] = Localization.Get("Workbench.Log.AllLevels");
        ClearCommand = new(log.Clear);
        log.Changed += OnLogChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    /// <summary>清空会话日志及未读提示。</summary>
    public RelayCommand ClearCommand { get; }
    /// <summary>符合当前级别和全文搜索条件的记录。</summary>
    public IReadOnlyList<AppLogEntry> Entries { get; private set; } = [];
    /// <summary>日志级别筛选选项。</summary>
    public IReadOnlyList<string> Levels => levels;
    /// <summary>当前筛选结果是否为空。</summary>
    public bool IsEmpty => Entries.Count == 0;
    /// <summary>是否可以复制选中记录。</summary>
    public bool HasSelection => SelectedEntry is not null;
    /// <summary>全局会话当前保留的未读错误数量。</summary>
    public int UnreadErrorCount => log.UnreadErrorCount;

    /// <summary>筛选索引；0 表示全部，其余索引对应 INFO、WARNING 和 ERROR。</summary>
    public int FilterIndex
    {
        get => filterIndex;
        set
        {
            if (!refreshingLevels && value is >= 0 and <= 3 && SetProperty(ref filterIndex, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>按时间、级别、来源、摘要和诊断详情搜索的文本。</summary>
    public string FilterText
    {
        get => filterText;
        set
        {
            if (SetProperty(ref filterText, value ?? string.Empty))
            {
                Refresh();
            }
        }
    }

    /// <summary>当前选中且仍在筛选结果中的记录。</summary>
    public AppLogEntry? SelectedEntry
    {
        get => selectedEntry;
        set
        {
            if (SetProperty(ref selectedEntry, value))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    /// <summary>返回当前筛选结果的完整文本，包含未展开的异常详情。</summary>
    public string CopyVisibleText()
    {
        return string.Join(Environment.NewLine + Environment.NewLine, Entries.Select(entry => entry.FullText));
    }

    /// <summary>注入面板当前是否正在被阅读的 UI 边界。</summary>
    public void SetReadingState(Func<bool>? value)
    {
        readingState = value;
        MarkVisibleEntriesRead();
    }

    /// <summary>仅当面板实际可见且所属窗口可阅读时清除未读标记。</summary>
    public void MarkVisibleEntriesRead()
    {
        if (!disposed && readingState?.Invoke() == true)
        {
            log.MarkRead();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
        readingState = null;
        log.Changed -= OnLogChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        refreshingLevels = true;

        try
        {
            levels[0] = Localization.Get("Workbench.Log.AllLevels");
            OnPropertyChanged(nameof(FilterIndex));
        }
        finally
        {
            refreshingLevels = false;
        }
    }

    private void OnLogChanged(object? sender, EventArgs e)
    {
        if (!disposed && Interlocked.CompareExchange(ref refreshPending, 1, 0) == 0)
        {
            Dispatcher.UIThread.Post(RefreshPending, DispatcherPriority.Background);
        }
    }

    private void RefreshPending()
    {
        Interlocked.Exchange(ref refreshPending, 0);
        Refresh();
    }

    private void Refresh()
    {
        if (disposed)
        {
            return;
        }

        var selected = SelectedEntry;
        var search = FilterText.Trim();
        Entries = log.Entries.Where(entry =>
            (FilterIndex == 0 || entry.Level == (AppLogLevel)(FilterIndex - 1)) &&
            (search.Length == 0 || entry.FullText.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
        OnPropertyChanged(nameof(Entries));
        SelectedEntry = selected is not null && Entries.Contains(selected) ? selected : null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(UnreadErrorCount));
        MarkVisibleEntriesRead();
    }
}
