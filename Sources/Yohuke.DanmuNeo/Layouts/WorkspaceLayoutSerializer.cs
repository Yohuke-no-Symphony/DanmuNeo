using Yohuke.DanmuNeo.I18n;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yohuke.DanmuNeo.Layouts;

internal static class WorkspaceLayoutSerializer
{
    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        MaxDepth = 128,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static string Serialize(WorkspaceLayoutFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!WorkspaceLayoutValidator.TryValidate(file, out _, out var errorKey))
        {
            throw LocalizedExceptionText.Create(errorKey, message => new InvalidDataException(message));
        }

        return JsonSerializer.Serialize(file, JSON_OPTIONS);
    }

    internal static WorkspaceLayoutFile Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json, new() { MaxDepth = JSON_OPTIONS.MaxDepth });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw LocalizedExceptionText.Create("Layout.Validation.ObjectRequired", message => new InvalidDataException(message));
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name.Equals(nameof(WorkspaceLayoutFile.Version), StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt32(out var version) && version > WorkspaceLayoutFile.CURRENT_VERSION)
            {
                throw LocalizedExceptionText.Create("Layout.Validation.FutureVersion",
                    message => new NotSupportedException(message), version);
            }
        }

        var file = document.Deserialize<WorkspaceLayoutFile>(JSON_OPTIONS);

        if (file is null)
        {
            throw LocalizedExceptionText.Create("Layout.Validation.EmptyFile", message => new InvalidDataException(message));
        }

        file = WorkspaceLayoutMigration.Upgrade(file);

        if (!WorkspaceLayoutValidator.TryValidate(file, out _, out var errorKey))
        {
            throw LocalizedExceptionText.Create(errorKey, message => new InvalidDataException(message));
        }

        return file;
    }

    internal static WorkspaceLayoutFile Copy(WorkspaceLayoutFile file)
    {
        return Deserialize(Serialize(file));
    }
}
