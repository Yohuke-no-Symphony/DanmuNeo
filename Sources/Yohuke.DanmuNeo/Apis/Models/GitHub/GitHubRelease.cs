using System.Text.Json.Serialization;

namespace Yohuke.DanmuNeo.Apis.Models.GitHub;

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; init; }

    [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; }

    [JsonPropertyName("draft")] public bool IsDraft { get; init; }

    [JsonPropertyName("prerelease")] public bool IsPrerelease { get; init; }
}