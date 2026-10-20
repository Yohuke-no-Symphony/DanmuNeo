using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证原生输入法窗口关联丢失后的恢复顺序和编辑状态边界。</summary>
public class WorkbenchInputMethodRecoveryTests
{
    /// <summary>恢复先关联原生窗口再重绑输入法，保留同一控件、客户端、焦点和编辑状态。</summary>
    [AvaloniaFact]
    public void NativeWindowAndClientAreRestoredWithoutChangingFocusTextOrSelection()
    {
        var operations = new List<string>();
        var input = new TextBox { Text = "布局调整后的中文草稿" };
        var enabled = new CheckBox { IsChecked = true };
        input.Bind(InputMethod.IsInputMethodEnabledProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = enabled });
        var window = new Window { Content = input, Width = 500, Height = 300 };

        try
        {
            window.Show();
            window.Activate();
            Assert.True(input.Focus());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var originalClient = GetClient(input);
            input.CaretIndex = 6;
            input.SelectionStart = 4;
            input.SelectionEnd = 6;
            input.PropertyChanged += (_, e) =>
            {
                if (e.Property == InputMethod.IsInputMethodEnabledProperty)
                {
                    operations.Add(e.GetNewValue<bool>() ? "client" : "release");
                }
            };
            Assert.True(WorkbenchTextInput.RestoreInputMethod(window, () =>
            {
                operations.Add("window");
                return true;
            }));

            Assert.Same(originalClient, GetClient(input));
            Assert.Equal(new[] { "window", "release", "client" }, operations);
            Assert.True(input.IsFocused);
            Assert.Equal("布局调整后的中文草稿", input.Text);
            Assert.Equal(6, input.CaretIndex);
            Assert.Equal(4, input.SelectionStart);
            Assert.Equal(6, input.SelectionEnd);
            input.SelectionStart = input.SelectionEnd = 6;
            originalClient.SetPreeditText("pinyin");
            Assert.Equal("pinyin", input.GetVisualDescendants().OfType<TextPresenter>().Single().PreeditText);
            originalClient.SetPreeditText(null);

            enabled.IsChecked = false;
            Assert.False(InputMethod.GetIsInputMethodEnabled(input));
            enabled.IsChecked = true;
            Assert.True(InputMethod.GetIsInputMethodEnabled(input));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>预编辑、显式禁用输入法或焦点已离开时不重置当前输入。</summary>
    [AvaloniaTheory]
    [InlineData("composition")]
    [InlineData("disabled")]
    [InlineData("focus")]
    public void IneligibleInputDoesNotReconnectNativeWindow(string state)
    {
        var input = new TextBox { Text = "保留草稿" };
        var button = new Button { Content = "其他操作" };
        var window = new Window
        {
            Content = new StackPanel { Children = { input, button } }, Width = 500, Height = 300
        };

        try
        {
            window.Show();
            window.Activate();
            Assert.True(input.Focus());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            if (state == "composition")
            {
                GetClient(input).SetPreeditText("pinyin");
            }
            else if (state == "disabled")
            {
                InputMethod.SetIsInputMethodEnabled(input, false);
            }
            else
            {
                Assert.True(button.Focus());
            }

            var called = false;
            Assert.False(WorkbenchTextInput.RestoreInputMethod(window, () => called = true));
            Assert.False(called);
            Assert.Equal("保留草稿", input.Text);
            Assert.Equal(state != "disabled", InputMethod.GetIsInputMethodEnabled(input));
            if (state == "composition")
            {
                Assert.Equal("pinyin", input.GetVisualDescendants().OfType<TextPresenter>().Single().PreeditText);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>布局导致上下文失效时结束孤立的预编辑，原生窗口尚不可恢复时保留预编辑和草稿。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidatedCompositionRecoveryPreservesCommittedDraft(bool nativeWindowReady)
    {
        var input = new TextBox { Text = "已经确认的中文" };
        var window = new Window { Content = input, Width = 500, Height = 300 };

        try
        {
            window.Show();
            window.Activate();
            Assert.True(input.Focus());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            input.CaretIndex = input.Text.Length;
            GetClient(input).SetPreeditText("pinyin");
            var caret = input.CaretIndex;
            Assert.False(WorkbenchTextInput.RestoreInputMethod(window, () => true));

            Assert.Equal(nativeWindowReady, WorkbenchTextInput.RestoreInputMethod(window, () => nativeWindowReady, true));

            var preedit = input.GetVisualDescendants().OfType<TextPresenter>().Single().PreeditText;
            Assert.Equal(nativeWindowReady ? null : "pinyin", preedit);
            Assert.Equal("已经确认的中文", input.Text);
            Assert.Equal(caret, input.CaretIndex);
            Assert.True(input.IsFocused);
            Assert.True(InputMethod.GetIsInputMethodEnabled(input));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>取消拖动也向实际宿主请求重新连接，不依赖布局模型发生修改。</summary>
    [AvaloniaFact]
    public void DragCompletionRequestsInputMethodRecoveryEvenWithoutLayoutMutation()
    {
        var host = new DockControl();
        WorkbenchDockTemplateCatalog.Install(host, new());
        var window = new Window { Content = host, Width = 500, Height = 300 };
        var requests = 0;
        var invalidations = 0;
        InputMethod.AddTextInputMethodClientRequeryRequestedHandler(window, (_, _) => requests++);
        window.AddHandler(WorkbenchTextInput.NativeContextInvalidatedEvent, (_, _) => invalidations++);

        try
        {
            window.Show();
            host.IsDraggingDock = true;
            Assert.Equal(0, requests);
            Assert.Equal(0, invalidations);
            host.IsDraggingDock = false;
            Assert.Equal(1, requests);
            Assert.Equal(1, invalidations);
        }
        finally
        {
            window.Close();
        }
    }

    private static TextInputMethodClient GetClient(TextBox input)
    {
        var query = new TextInputMethodClientRequestedEventArgs
        {
            RoutedEvent = InputElement.TextInputMethodClientRequestedEvent
        };
        input.RaiseEvent(query);
        return Assert.IsAssignableFrom<TextInputMethodClient>(query.Client);
    }
}
