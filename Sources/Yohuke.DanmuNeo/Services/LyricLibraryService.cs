using Yohuke.DanmuNeo.I18n;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 管理本地歌词库。
/// </summary>
public class LyricLibraryService
{
    private static readonly Regex METADATA_PATTERN = new(@"^\[[a-zA-Z]+:.*\]$", RegexOptions.Compiled);
    private static readonly Regex TIMELINE_PATTERN = new(@"\[\d+:\d+(\.\d+)?\]", RegexOptions.Compiled);
    private static readonly Regex BROKEN_TIMELINE_PATTERN = new(@"\[\d+:\d+[^\]]*$|\[\d+:\d+[^\]]*\]", RegexOptions.Compiled);

    /// <summary>
    /// 按关键词搜索歌词库。
    /// </summary>
    public List<LyricLibraryItem> Search(IEnumerable<LyricLibraryItem> library, string keyword)
    {
        var words = keyword
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return library
                .OrderByDescending(item => item.LastUsedAt ?? item.UpdatedAt)
                .ToList();
        }

        return library
            .Where(item => words.All(word => GetSearchText(item).Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.LastUsedAt ?? item.UpdatedAt)
            .ToList();
    }

    /// <summary>
    /// 新增或更新歌词库条目。
    /// </summary>
    public LyricLibraryItem Upsert(List<LyricLibraryItem> library, LyricLibraryItem item)
    {
        NormalizeMultilingualLyrics(item);
        Validate(item);

        var now = DateTimeOffset.Now;
        var existing = FindExisting(library, item);

        if (existing is null)
        {
            item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;
            item.CreatedAt = now;
            item.UpdatedAt = now;
            library.Add(item);
            return item;
        }

        existing.Title = item.Title;
        existing.Artist = item.Artist;
        existing.Album = item.Album;
        existing.Source = item.Source;
        existing.SourceSongId = item.SourceSongId;
        existing.Tags = item.Tags;
        existing.HasTranslation = item.HasTranslation;
        existing.LyricText = item.LyricText;
        existing.TranslatedLyricText = item.TranslatedLyricText;
        existing.UpdatedAt = now;
        return existing;
    }

    /// <summary>
    /// 从本地歌词文件创建歌词库条目。
    /// </summary>
    public async Task<LyricLibraryItem> CreateFromLocalFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken);
        var title = Path.GetFileNameWithoutExtension(filePath);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw LocalizedExceptionText.Create("Services.LyricLibrary.EmptyFile",
                message => new InvalidDataException(message));
        }

        if (LooksLikeLocalXml(text))
        {
            return CreateFromLocalXml(text, title);
        }

        var item = new LyricLibraryItem
        {
            Title = title,
            Source = "local",
            LyricText = text,
            Tags = title
        };
        NormalizeMultilingualLyrics(item);
        return item;
    }

    /// <summary>
    /// 标记歌词条目已使用。
    /// </summary>
    public void Touch(LyricLibraryItem item)
    {
        item.LastUsedAt = DateTimeOffset.Now;
    }

    /// <summary>
    /// 校验歌词库条目。
    /// </summary>
    public void Validate(LyricLibraryItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Title))
        {
            throw LocalizedExceptionText.Create("Services.LyricLibrary.TitleRequired",
                message => new InvalidDataException(message));
        }

        if (string.IsNullOrWhiteSpace(item.LyricText) &&
            string.IsNullOrWhiteSpace(item.TranslatedLyricText))
        {
            throw LocalizedExceptionText.Create("Services.LyricLibrary.ContentRequired",
                message => new InvalidDataException(message));
        }

        ValidateLyricText(item.LyricText, "Services.LyricLibrary.OriginalLyrics");
        ValidateLyricText(item.TranslatedLyricText, "Services.LyricLibrary.TranslatedLyrics");
    }

    private static LyricLibraryItem? FindExisting(List<LyricLibraryItem> library, LyricLibraryItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourceSongId))
        {
            var bySource = library.FirstOrDefault(existing =>
                existing.Source == item.Source &&
                existing.SourceSongId == item.SourceSongId);

            if (bySource is not null)
            {
                return bySource;
            }
        }

        return library.FirstOrDefault(existing =>
            existing.Source == item.Source &&
            existing.Title.Equals(item.Title, StringComparison.OrdinalIgnoreCase) &&
            existing.Artist.Equals(item.Artist, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetSearchText(LyricLibraryItem item)
    {
        return string.Join(" ", item.Title, item.Artist, item.Album, item.SourceLabel, item.SourceSongId, item.Tags);
    }

    private static bool LooksLikeLocalXml(string text)
    {
        return text.Contains("<local", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<lyric>", StringComparison.OrdinalIgnoreCase);
    }

    private static LyricLibraryItem CreateFromLocalXml(string text, string fallbackTitle)
    {
        XElement root;

        try
        {
            root = XElement.Parse(text);
        }
        catch (Exception exception)
        {
            throw LocalizedExceptionText.Create("Services.LyricLibrary.XmlParseFailed",
                message => new InvalidDataException(message, exception),
                new Func<string>(() => LocalizedExceptionText.Get(exception)));
        }

        var title = ReadElement(root, "name");
        var artist = ReadElement(root, "artists");
        var tags = ReadElement(root, "tags");
        var type = ReadElement(root, "type");
        var lyric = ReadElement(root, "lyric");

        var item = new LyricLibraryItem
        {
            Title = string.IsNullOrWhiteSpace(title) ? fallbackTitle : title,
            Artist = artist,
            Source = "local",
            Tags = tags,
            HasTranslation = type == "双语",
            LyricText = lyric
        };
        NormalizeMultilingualLyrics(item);
        return item;
    }

    private static void NormalizeMultilingualLyrics(LyricLibraryItem item)
    {
        if (!item.HasTranslation ||
            !string.IsNullOrWhiteSpace(item.TranslatedLyricText) ||
            string.IsNullOrWhiteSpace(item.LyricText))
        {
            return;
        }

        var groups = item.LyricText
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => new
            {
                Line = line,
                Timeline = TIMELINE_PATTERN.Match(line).Value
            })
            .Where(lineInfo => !string.IsNullOrWhiteSpace(lineInfo.Timeline))
            .GroupBy(lineInfo => lineInfo.Timeline)
            .ToList();

        if (groups.Count == 0 || groups.Any(group => group.Count() != 2))
        {
            return;
        }

        item.LyricText = string.Join('\n', groups.Select(group => group.First().Line));
        item.TranslatedLyricText = string.Join('\n', groups.Select(group => group.Skip(1).First().Line));
    }

    private static void ValidateLyricText(string text, string nameKey)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var hasContent = false;

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || METADATA_PATTERN.IsMatch(line))
            {
                continue;
            }

            if (line.Contains('[') != line.Contains(']'))
            {
                throw LocalizedExceptionText.Create("Services.LyricLibrary.IncompleteTimestamp",
                    message => new InvalidDataException(message), new Func<string>(() => Localization.Get(nameKey)));
            }

            if (BROKEN_TIMELINE_PATTERN.IsMatch(line) && !TIMELINE_PATTERN.IsMatch(line))
            {
                throw LocalizedExceptionText.Create("Services.LyricLibrary.InvalidTimestamp",
                    message => new InvalidDataException(message), new Func<string>(() => Localization.Get(nameKey)));
            }

            var content = TIMELINE_PATTERN.Replace(line, "").Trim();

            if (!string.IsNullOrWhiteSpace(content))
            {
                hasContent = true;
            }
        }

        if (!hasContent)
        {
            throw LocalizedExceptionText.Create("Services.LyricLibrary.NoUsableContent",
                message => new InvalidDataException(message), new Func<string>(() => Localization.Get(nameKey)));
        }
    }

    private static string ReadElement(XElement root, string name)
    {
        return root.Element(name)?.Value.Trim() ?? "";
    }
}
