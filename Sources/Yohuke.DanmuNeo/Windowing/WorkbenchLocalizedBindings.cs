using Avalonia;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class WorkbenchLocalizedBindings : IDisposable
{
    private readonly Dictionary<(AvaloniaObject Target, AvaloniaProperty Property), IDisposable> bindings = [];
    private bool disposed;

    internal void Bind(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        Bind(target, property, () => Localization.Get(key));
    }

    internal void Bind(AvaloniaObject target, AvaloniaProperty property, Func<string> textProvider)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var key = (target, property);

        if (bindings.Remove(key, out var previous))
        {
            previous.Dispose();
        }

        bindings.Add(key, target.Bind(property, Localization.Observe(textProvider).ToBinding()));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        foreach (var binding in bindings.Values)
        {
            binding.Dispose();
        }

        bindings.Clear();
    }
}
