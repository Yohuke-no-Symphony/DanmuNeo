using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>
/// 弹幕转发规则视图模型。
/// </summary>
public partial class DanmuForwardRuleViewModel : ViewModelBase
{
    private readonly Action<DanmuForwardRuleViewModel> onChanged;
    private IReadOnlyList<ForwardSourceRoomOption> sourceRoomOptions = [];
    private IReadOnlyList<ForwardAccountOption> accountOptions = [];
    private IEnumerable<MarkSymbolGroup> symbolGroups = [];

    /// <summary>
    /// 初始化弹幕转发规则视图模型。
    /// </summary>
    public DanmuForwardRuleViewModel(DanmuForwardRuleState state, Action<DanmuForwardRuleViewModel> onChanged)
    {
        State = state;
        this.onChanged = onChanged;
        statusText = state.IsEnabled ? "等待启动" : "未启用";
    }

    /// <summary>
    /// 转发规则状态。
    /// </summary>
    public DanmuForwardRuleState State { get; }

    /// <summary>
    /// 规则 ID。
    /// </summary>
    public string Id => State.Id;

    /// <summary>
    /// 与目标直播间共享的来源选项。
    /// </summary>
    public IReadOnlyList<ForwardSourceRoomOption> SourceRoomOptions
    {
        get => sourceRoomOptions;
        private set => SetProperty(ref sourceRoomOptions, value);
    }

    /// <summary>
    /// 与主窗口共享的转发发送账号选项。
    /// </summary>
    public IReadOnlyList<ForwardAccountOption> AccountOptions
    {
        get => accountOptions;
        private set => SetProperty(ref accountOptions, value);
    }

    /// <summary>
    /// 与主窗口共享的符号组选项。
    /// </summary>
    public IEnumerable<MarkSymbolGroup> SymbolGroups
    {
        get => symbolGroups;
        private set => SetProperty(ref symbolGroups, value);
    }

    /// <summary>
    /// 源直播间选项键。
    /// </summary>
    public string? SourceRoomKey
    {
        get => string.IsNullOrWhiteSpace(State.SourceWorkspaceId) || string.IsNullOrWhiteSpace(State.SourceRoomStateId)
            ? ""
            : $"{State.SourceWorkspaceId}|{State.SourceRoomStateId}";
        set
        {
            var normalizedValue = value ?? "";

            if (string.IsNullOrWhiteSpace(normalizedValue))
            {
                return;
            }

            if (SourceRoomKey == normalizedValue)
            {
                return;
            }

            var segments = normalizedValue.Split('|', 2);
            State.SourceWorkspaceId = segments.Length > 0 ? segments[0] : "";
            State.SourceRoomStateId = segments.Length > 1 ? segments[1] : "";
            OnPropertyChanged();
            onChanged(this);
        }
    }

    /// <summary>
    /// 是否启用。
    /// </summary>
    public bool IsEnabled
    {
        get => State.IsEnabled;
        set
        {
            if (State.IsEnabled == value)
            {
                return;
            }

            State.IsEnabled = value;
            OnPropertyChanged();
            onChanged(this);
        }
    }

    /// <summary>
    /// 转发时使用的符号组 ID。
    /// </summary>
    [AllowNull]
    public string MarkSymbolGroupId
    {
        get => State.MarkSymbolGroupId;
        set
        {
            if (value is null || State.MarkSymbolGroupId == value)
            {
                return;
            }

            State.MarkSymbolGroupId = value;
            OnPropertyChanged();
            onChanged(this);
        }
    }

    /// <summary>
    /// 转发发送时指定的账号 ID。
    /// </summary>
    [AllowNull]
    public string AccountOverrideId
    {
        get => State.AccountOverrideId ?? "";
        set
        {
            if (value is null || State.AccountOverrideId == value)
            {
                return;
            }

            State.AccountOverrideId = value;
            OnPropertyChanged();
            onChanged(this);
        }
    }

    /// <summary>
    /// 监听发送人 UID。
    /// </summary>
    public string SenderUid
    {
        get => State.SenderUid;
        set
        {
            if (State.SenderUid == value)
            {
                return;
            }

            State.SenderUid = value;
            OnPropertyChanged();
            onChanged(this);
        }
    }

    /// <summary>
    /// 同传格式正则。
    /// </summary>
    public string ContentPattern
    {
        get => State.ContentPattern;
        set
        {
            if (State.ContentPattern == value)
            {
                return;
            }

            State.ContentPattern = value;
            OnPropertyChanged();
            onChanged(this);
        }
    }

    [ObservableProperty] private string statusText;

    internal void ConfigureOptions(
        IReadOnlyList<ForwardSourceRoomOption> sources,
        IReadOnlyList<ForwardAccountOption> accounts,
        IEnumerable<MarkSymbolGroup> groups)
    {
        SourceRoomOptions = sources;
        AccountOptions = accounts;
        SymbolGroups = groups;
    }
}
