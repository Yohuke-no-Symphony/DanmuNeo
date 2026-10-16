namespace Yohuke.DanmuNeo.Models.State;

/// <summary>
/// 应用全局设置。
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 在 macOS 主窗口中显示菜单，默认使用原生系统菜单。
    /// </summary>
    public bool UseWindowMenuOnMac { get; set; }

    /// <summary>
    /// 是否在启动时检查应用正式发布版本。
    /// </summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// 同传开标记。
    /// </summary>
    public string TranslateOpenMark { get; set; } = "【";

    /// <summary>
    /// 同传闭标记。
    /// </summary>
    public string TranslateCloseMark { get; set; } = "】";

    /// <summary>
    /// 歌词开标记。
    /// </summary>
    public string LyricOpenMark { get; set; } = "【♪";

    /// <summary>
    /// 歌词闭标记。
    /// </summary>
    public string LyricCloseMark { get; set; } = "】";

    /// <summary>
    /// 弹幕插入开标记。
    /// </summary>
    public string DanmuInsertOpenMark { get; set; } = "\"";

    /// <summary>
    /// 弹幕插入闭标记。
    /// </summary>
    public string DanmuInsertCloseMark { get; set; } = "\"";

    /// <summary>
    /// 可选符号组。
    /// </summary>
    public List<MarkSymbolGroup> MarkGroups { get; set; } = [];

    /// <summary>
    /// 快捷键绑定。
    /// </summary>
    public List<ShortcutBindingState> ShortcutBindings { get; set; } = [];

    /// <summary>
    /// 快捷键绑定版本。
    /// </summary>
    public int ShortcutBindingsVersion { get; set; }

    /// <summary>
    /// 默认歌词来源。
    /// </summary>
    public string DefaultLyricSource { get; set; } = "wy";

    /// <summary>
    /// 弹幕发送间隔。
    /// </summary>
    public int SendIntervalMs { get; set; } = 750;

    /// <summary>
    /// 请求超时秒数。
    /// </summary>
    public double TimeoutSeconds { get; set; } = 3.05;

    /// <summary>
    /// 是否启用屏蔽词处理。
    /// </summary>
    public bool EnableShieldProcessing { get; set; } = true;

    /// <summary>
    /// 弹幕被屏蔽时是否重发。
    /// </summary>
    public bool ResendWhenShielded { get; set; } = true;

    /// <summary>
    /// 播放或追帧时是否自动开启监听。
    /// </summary>
    public bool AutoStartListeningWithLivePlayer { get; set; } = true;

    /// <summary>
    /// 是否使用紧凑弹幕显示。
    /// </summary>
    public bool CompactDanmuDisplay { get; set; }

    /// <summary>
    /// 紧凑模式下是否允许普通弹幕自动换行。
    /// </summary>
    public bool CompactDanmuWrap { get; set; }

    /// <summary>
    /// 紧凑模式下是否允许醒目留言自动换行。
    /// </summary>
    public bool CompactSuperChatWrap { get; set; } = true;

    /// <summary>
    /// 是否屏蔽仅包含表情标记的普通弹幕。
    /// </summary>
    public bool HideEmoticonDanmu { get; set; }

    /// <summary>
    /// 屏蔽词替换规则。
    /// </summary>
    public List<ShieldReplacementRule> ShieldReplacementRules { get; set; } = [];

    /// <summary>
    /// 是否对歌词应用屏蔽词替换。
    /// </summary>
    public bool ApplyShieldReplacementToLyrics { get; set; } = true;

    /// <summary>
    /// 每个配置文件保留的自动备份数量。
    /// </summary>
    public int BackupRetentionCount { get; set; } = 10;

    /// <summary>
    /// QQ 音乐 Cookie。
    /// </summary>
    public string QQMusicCookie { get; set; } = "";

    /// <summary>
    /// QQ 音乐 Cookie 绑定状态。
    /// </summary>
    public string QQMusicCookieStatus => string.IsNullOrWhiteSpace(QQMusicCookie) ? "未绑定" : "已绑定";

    /// <summary>
    /// QQ 音乐 Cookie 摘要。
    /// </summary>
    public string QQMusicCookieSummary => string.IsNullOrWhiteSpace(QQMusicCookie)
        ? "尚未读取 QQ 音乐 Cookie"
        : $"Cookie 尾段 {QQMusicCookie[^Math.Min(8, QQMusicCookie.Length)..]}";

    /// <summary>
    /// 配色方案。
    /// </summary>
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    /// <summary>
    /// 左侧栏宽度。
    /// </summary>
    public double SidebarWidth { get; set; } = 240;

    /// <summary>
    /// 是否折叠整个工作区侧栏。
    /// </summary>
    public bool IsSidebarCollapsed { get; set; }

    /// <summary>
    /// 主窗口宽度。
    /// </summary>
    public double WindowWidth { get; set; } = 1280;

    /// <summary>
    /// 主窗口高度。
    /// </summary>
    public double WindowHeight { get; set; } = 820;

    /// <summary>
    /// 工作区直播区域列宽。
    /// </summary>
    public double WorkspaceLiveColumnWidth { get; set; }

    /// <summary>
    /// 工作区工具区域列宽。
    /// </summary>
    public double WorkspaceToolColumnWidth { get; set; }
}
