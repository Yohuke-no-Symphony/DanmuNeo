using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>
/// 转发发送账号选项。
/// </summary>
public class ForwardAccountOption : ObservableObject, IDisposable
{
    /// <summary>
    /// 创建关联账号的选项；未指定账号时使用目标直播间账号。
    /// </summary>
    public ForwardAccountOption(BilibiliAccount? account = null)
    {
        Account = account;

        if (account is not null)
        {
            account.PropertyChanged += OnAccountPropertyChanged;
        }
    }

    internal BilibiliAccount? Account { get; }

    /// <summary>
    /// 账号 ID。
    /// </summary>
    public string AccountId => Account?.Id ?? "";

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string DisplayName => Account?.Name ?? "使用目标直播间账号";

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Account is not null)
        {
            Account.PropertyChanged -= OnAccountPropertyChanged;
        }

        GC.SuppressFinalize(this);
    }

    private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(BilibiliAccount.Name))
        {
            OnPropertyChanged(nameof(DisplayName));
        }

        if (e.PropertyName is null or "" or nameof(BilibiliAccount.Id))
        {
            OnPropertyChanged(nameof(AccountId));
        }
    }
}
