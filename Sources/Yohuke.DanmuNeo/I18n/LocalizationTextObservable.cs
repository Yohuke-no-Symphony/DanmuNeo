namespace Yohuke.DanmuNeo.I18n;

internal sealed class LocalizationTextObservable(Func<string> textProvider) : IObservable<string>
{
    /// <inheritdoc/>
    public IDisposable Subscribe(IObserver<string> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        return new LocalizationTextSubscription(textProvider, observer);
    }
}
