using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Logging;

namespace Yohuke.DanmuNeo.Services;

internal sealed class AppLogAvaloniaSink(IAppLogService log) : ILogSink
{
    /// <inheritdoc/>
    public bool IsEnabled(LogEventLevel level, string area)
    {
        return level >= LogEventLevel.Warning;
    }

    /// <inheritdoc/>
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        Log(level, area, source, messageTemplate, []);
    }

    /// <inheritdoc/>
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
        params object?[] propertyValues)
    {
        if (!IsEnabled(level, area))
        {
            return;
        }

        var index = 0;
        var message = Regex.Replace(messageTemplate, @"\{[^{}]+\}", match =>
            index < propertyValues.Length
                ? Convert.ToString(propertyValues[index++], CultureInfo.InvariantCulture) ?? "<null>"
                : match.Value);
        var origin = $"Avalonia/{area}";

        if (level >= LogEventLevel.Error)
        {
            log.Error(origin, message, propertyValues.OfType<Exception>().FirstOrDefault());
        }
        else
        {
            log.Warning(origin, message);
        }
    }
}
