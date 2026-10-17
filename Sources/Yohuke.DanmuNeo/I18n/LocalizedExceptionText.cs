namespace Yohuke.DanmuNeo.I18n;

internal static class LocalizedExceptionText
{
    internal static TException Create<TException>(string key, Func<string, TException> create,
        params object[] arguments) where TException : Exception
    {
        string Render()
        {
            return Localization.Format(key, arguments.Select(argument =>
                argument is Func<string> provider ? provider() : argument).ToArray());
        }

        var error = create(Render());
        error.Data[typeof(LocalizedExceptionText)] = (Func<string>)Render;
        return error;
    }

    internal static string Get(Exception error)
    {
        return error.Data[typeof(LocalizedExceptionText)] is Func<string> provider ? provider() : error.Message;
    }
}
