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

        if (!WorkspaceLayoutValidator.TryValidate(file, out var error))
        {
            throw new InvalidDataException(error);
        }

        return JsonSerializer.Serialize(file, JSON_OPTIONS);
    }

    internal static WorkspaceLayoutFile Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json, new() { MaxDepth = JSON_OPTIONS.MaxDepth });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("布局文件必须是 JSON 对象。");
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name.Equals(nameof(WorkspaceLayoutFile.Version), StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt32(out var version) && version > WorkspaceLayoutFile.CURRENT_VERSION)
            {
                throw new NotSupportedException($"布局文件版本 {version} 高于当前支持的版本，已保留原文件。");
            }
        }

        var file = document.Deserialize<WorkspaceLayoutFile>(JSON_OPTIONS);

        if (file is null)
        {
            throw new InvalidDataException("布局文件为空。");
        }

        file = WorkspaceLayoutMigration.Upgrade(file);

        if (!WorkspaceLayoutValidator.TryValidate(file, out var error))
        {
            throw new InvalidDataException(error);
        }

        return file;
    }

    internal static WorkspaceLayoutFile Copy(WorkspaceLayoutFile file)
    {
        return Deserialize(Serialize(file));
    }
}
