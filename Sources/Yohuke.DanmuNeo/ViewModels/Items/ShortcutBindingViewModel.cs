using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>
/// 快捷键绑定视图模型。
/// </summary>
public partial class ShortcutBindingViewModel : ObservableObject
{
    private readonly Action<ShortcutBindingViewModel> onChanged;
    private bool isRecording;
    private bool isConflicted;

    /// <summary>
    /// 初始化快捷键绑定视图模型。
    /// </summary>
    public ShortcutBindingViewModel(
        ShortcutActionDefinition definition,
        ShortcutBindingState binding,
        Action<ShortcutBindingViewModel> onChanged)
    {
        Definition = definition;
        Binding = binding;
        this.onChanged = onChanged;
    }

    /// <summary>
    /// 快捷键动作定义。
    /// </summary>
    public ShortcutActionDefinition Definition { get; }

    /// <summary>
    /// 快捷键绑定状态。
    /// </summary>
    public ShortcutBindingState Binding { get; }

    /// <summary>
    /// 动作键。
    /// </summary>
    public string ActionKey => Definition.ActionKey;

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name => Definition.Name;

    /// <summary>
    /// 默认快捷键文本。
    /// </summary>
    public string DefaultGestureText => Definition.DefaultGestureText;

    /// <summary>
    /// 快捷键显示文本。
    /// </summary>
    public string DisplayGestureText => ShortcutBindingService.FormatDisplayText(Binding.GestureText);

    /// <summary>
    /// 是否启用。
    /// </summary>
    public bool IsEnabled
    {
        get => Binding.IsEnabled;
        set
        {
            if (Binding.IsEnabled == value)
            {
                return;
            }

            Binding.IsEnabled = value;
            OnPropertyChanged();
            onChanged(this);
        }
    }

    /// <summary>
    /// 是否正在录制。
    /// </summary>
    public bool IsRecording
    {
        get => isRecording;
        set => SetProperty(ref isRecording, value);
    }

    /// <summary>
    /// 是否存在冲突。
    /// </summary>
    public bool IsConflicted
    {
        get => isConflicted;
        private set
        {
            if (!SetProperty(ref isConflicted, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ConflictText));
        }
    }

    /// <summary>
    /// 冲突提示。
    /// </summary>
    public string ConflictText => IsConflicted ? Localization.Get("Shortcut.Conflict") : "";

    /// <summary>
    /// 在现有绑定上刷新语言，不修改快捷键或录制状态。
    /// </summary>
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DefaultGestureText));
        OnPropertyChanged(nameof(DisplayGestureText));
        OnPropertyChanged(nameof(ConflictText));
    }

    /// <summary>
    /// 设置快捷键文本。
    /// </summary>
    public void SetGestureText(string gestureText, bool isEnabled)
    {
        Binding.GestureText = gestureText;
        Binding.IsEnabled = isEnabled;
        OnPropertyChanged(nameof(DisplayGestureText));
        OnPropertyChanged(nameof(IsEnabled));
        onChanged(this);
    }

    /// <summary>
    /// 设置冲突状态。
    /// </summary>
    public void SetConflicted(bool value)
    {
        IsConflicted = value;
    }
}
