namespace Yohuke.DanmuNeo.Tests;

internal sealed class LocalizationTextObserver : IObserver<string>
{
    internal List<string> Values { get; } = [];

    /// <inheritdoc/>
    public void OnNext(string value)
    {
        Values.Add(value);
    }

    /// <inheritdoc/>
    public void OnError(Exception error)
    {
        throw new InvalidOperationException("The localization stream failed.", error);
    }

    /// <inheritdoc/>
    public void OnCompleted()
    {
    }
}
