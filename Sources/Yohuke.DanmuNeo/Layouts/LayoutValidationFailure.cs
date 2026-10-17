using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Layouts;

internal sealed record LayoutValidationFailure(string LocalizationKey)
{
    internal static ArgumentException CreateException(string key, string? parameterName = null)
    {
        var exception = LocalizedExceptionText.Create(key, message => new ArgumentException(message, parameterName));
        exception.Data[typeof(LayoutValidationFailure)] = new LayoutValidationFailure(key);
        return exception;
    }

    internal static string GetDisplayMessage(Exception exception)
    {
        return exception.Data[typeof(LayoutValidationFailure)] is LayoutValidationFailure failure
            ? Localization.Get(failure.LocalizationKey)
            : LocalizedExceptionText.Get(exception);
    }
}
