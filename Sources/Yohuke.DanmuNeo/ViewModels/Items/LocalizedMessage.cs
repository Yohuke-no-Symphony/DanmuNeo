using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.ViewModels.Items;

internal sealed class LocalizedMessage(string key, params object[] arguments)
{
    internal string Render()
    {
        if (string.IsNullOrEmpty(key))
        {
            return "";
        }

        var values = arguments.Select(argument => argument switch
        {
            LocalizedMessage message => message.Render(),
            Func<string> textProvider => textProvider(),
            _ => argument
        }).ToArray();
        return values.Length == 0 ? Localization.Get(key) : Localization.Format(key, values);
    }
}
