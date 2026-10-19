using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Models.State;

/// <summary>
/// B 站账号配置。
/// </summary>
public partial class BilibiliAccount : ObservableObject
{
    private string id = Guid.NewGuid().ToString("N");
    private string name = "账号";
    private string cookie = "";
    private bool isGlobalDefault;
    private bool autoSwitchMedal;
    private bool autoNameOnLogin;

    /// <summary>
    /// 账号 ID。
    /// </summary>
    public string Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    /// <summary>
    /// 账号名称。
    /// </summary>
    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    /// <summary>仅新建账号等待首次有效登录时采用用户名，旧配置和自定义名称默认保留。</summary>
    public bool AutoNameOnLogin
    {
        get => autoNameOnLogin;
        set => SetProperty(ref autoNameOnLogin, value);
    }

    /// <summary>
    /// Cookie 文本。
    /// </summary>
    public string Cookie
    {
        get => cookie;
        set
        {
            if (SetProperty(ref cookie, value))
            {
                OnPropertyChanged(nameof(CookieStatus));
                OnPropertyChanged(nameof(CookieSummary));
            }
        }
    }

    /// <summary>
    /// Cookie 绑定状态。
    /// </summary>
    public string CookieStatus => Localization.Get(string.IsNullOrWhiteSpace(Cookie)
        ? "App.Cookie.NotBound" : "App.Cookie.Bound");

    /// <summary>
    /// Cookie 摘要。
    /// </summary>
    public string CookieSummary => string.IsNullOrWhiteSpace(Cookie)
        ? Localization.Get("App.Cookie.EmptyAccount")
        : Localization.Format("App.Cookie.Summary", Cookie[^Math.Min(8, Cookie.Length)..]);

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(CookieStatus));
        OnPropertyChanged(nameof(CookieSummary));
    }

    /// <summary>是否仅随活跃房间自动佩戴对应粉丝牌，默认关闭。</summary>
    public bool AutoSwitchMedal
    {
        get => autoSwitchMedal;
        set => SetProperty(ref autoSwitchMedal, value);
    }

    /// <summary>
    /// 是否为全局默认账号。
    /// </summary>
    public bool IsGlobalDefault
    {
        get => isGlobalDefault;
        set => SetProperty(ref isGlobalDefault, value);
    }
}
