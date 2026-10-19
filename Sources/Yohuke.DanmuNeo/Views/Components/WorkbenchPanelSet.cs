using Avalonia.Controls;
using Avalonia.Input;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 工作台唯一持有的业务视图，布局操作只重组这些实例。
/// </summary>
public sealed class WorkbenchPanelSet : IDisposable
{
    private readonly WorkbenchContext context;
    private readonly WorkbenchInteractionCoordinator interactions;

    /// <summary>
    /// 为共享上下文创建一组可复用面板。
    /// </summary>
    public WorkbenchPanelSet(WorkbenchContext context)
    {
        this.context = context;
        Composer = new(context);
        interactions = new(context, Composer);
        Player = new(context);
        SuperChat = new(context, interactions);
        Danmu = new(context, interactions);
        History = new(context);
        Lyrics = new(context);
        Forwarding = new(context);
        Moderation = new(context, context.Application.ModerationManager);
        AccountMedals = new(context, context.Application.AccountMedals);
        Panels = new Dictionary<string, WorkbenchPanelView>
        {
            [WorkbenchPanelIds.LIVE_PLAYER] = Player,
            [WorkbenchPanelIds.COMPOSER] = Composer,
            [WorkbenchPanelIds.SUPER_CHAT] = SuperChat,
            [WorkbenchPanelIds.DANMU] = Danmu,
            [WorkbenchPanelIds.HISTORY] = History,
            [WorkbenchPanelIds.LYRICS] = Lyrics,
            [WorkbenchPanelIds.FORWARDING] = Forwarding,
            [WorkbenchPanelIds.MODERATION] = Moderation,
            [WorkbenchPanelIds.ACCOUNT_MEDALS] = AccountMedals
        };
        Lyrics.LyricLibraryRequested += owner => LyricLibraryRequested?.Invoke(owner);
        context.SelectionChanged += Context_OnSelectionChanged;
    }

    /// <summary>追帧面板。</summary>
    public LivePlayerPanelView Player { get; }

    /// <summary>输入面板。</summary>
    public DraftComposerPanelView Composer { get; }

    /// <summary>SC 面板。</summary>
    public SuperChatPanelView SuperChat { get; }

    /// <summary>弹幕面板。</summary>
    public DanmuPanelView Danmu { get; }

    /// <summary>历史面板。</summary>
    public TranslateHistoryPanelView History { get; }

    /// <summary>歌词面板。</summary>
    public LyricsPanelView Lyrics { get; }

    /// <summary>转发面板。</summary>
    public ForwardingPanelView Forwarding { get; }

    /// <summary>按所选账号管理跨房间禁言的工具面板。</summary>
    public ModerationPanelView Moderation { get; }

    /// <summary>当前账号或指定账号的粉丝牌面板。</summary>
    public AccountMedalsPanelView AccountMedals { get; }

    /// <summary>以稳定面板 ID 索引的唯一视图集合。</summary>
    public IReadOnlyDictionary<string, WorkbenchPanelView> Panels { get; }

    /// <summary>请求打开单实例歌词库窗口。</summary>
    public event Action<Window?>? LyricLibraryRequested;

    /// <summary>注入显示面板及激活所属窗口的入口。</summary>
    public void SetPanelActivator(Action<string> activate)
    {
        interactions.SetPanelActivator(activate);
    }

    /// <summary>在布局重组前保持原生播放器宿主。</summary>
    public IDisposable BeginLayoutMigration()
    {
        return Player.BeginLayoutMigration();
    }

    /// <summary>交互锁定时结束长按操作，保留面板显示。</summary>
    public void SetInteractionBlocked(bool blocked)
    {
        if (blocked)
        {
            StopHeldActions();
        }
    }

    /// <summary>显示并聚焦输入面板。</summary>
    public void FocusInputDraft()
    {
        interactions.FocusInputDraft();
    }

    /// <summary>清空当前房间输入草稿。</summary>
    public void ClearInputDraft()
    {
        interactions.ClearInputDraft();
    }

    /// <summary>跳到下一句歌词，按住时不重复执行。</summary>
    public void SeekNextLyric(Key key)
    {
        Lyrics.SeekNextLyric(key);
    }

    /// <summary>开始长按歌词时间微调。</summary>
    public void StartLyricSeek(Key key, double offsetSeconds)
    {
        Lyrics.StartLyricSeek(key, offsetSeconds);
    }

    /// <summary>向长按控制器传递按键释放。</summary>
    public void ReleaseKey(Key key)
    {
        Lyrics.ReleaseKey(key);
    }

    /// <summary>结束所有按住动作。</summary>
    public void StopHeldActions()
    {
        Lyrics.StopHeldActions();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        context.SelectionChanged -= Context_OnSelectionChanged;

        foreach (var panel in Panels.Values)
        {
            panel.Dispose();
        }
    }

    private void Context_OnSelectionChanged(object? sender, EventArgs e)
    {
        StopHeldActions();
    }
}
