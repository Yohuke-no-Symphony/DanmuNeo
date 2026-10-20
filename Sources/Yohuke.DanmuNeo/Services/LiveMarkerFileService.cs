using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using Yohuke.DanmuNeo.Models.LiveMarker;

namespace Yohuke.DanmuNeo.Services;

/// <summary>兼容旧 LiveMarker YAML 的纯存储服务，所有时间字段均按原值独立处理。</summary>
public sealed class LiveMarkerFileService : ILiveMarkerFileService
{
    private static readonly Regex dateTimePattern = new(
        @"^\d{4}-\d{2}-\d{2}[Tt ]\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:[Zz]|[+\-]\d{2}:\d{2})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex totalHoursPattern = new(
        @"^(?<hours>\d+):(?<minutes>\d{2}):(?<seconds>\d{2})(?:\.(?<fraction>\d{1,7}))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly object leaseLock = new();
    private readonly Dictionary<string, (object Owner, int Count)> leases =
        new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    /// <inheritdoc/>
    public IDisposable Acquire(string filePath, object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var normalizedPath = NormalizePath(filePath);

        lock (leaseLock)
        {
            if (leases.TryGetValue(normalizedPath, out var existing))
            {
                if (!ReferenceEquals(existing.Owner, owner))
                {
                    throw new IOException("The live marker file is already open in another room.");
                }

                leases[normalizedPath] = (owner, checked(existing.Count + 1));
            }
            else
            {
                leases.Add(normalizedPath, (owner, 1));
            }
        }

        return new LiveMarkerFileLease(() =>
        {
            lock (leaseLock)
            {
                var existing = leases[normalizedPath];

                if (existing.Count == 1)
                {
                    leases.Remove(normalizedPath);
                }
                else
                {
                    leases[normalizedPath] = (owner, existing.Count - 1);
                }
            }
        });
    }

    /// <inheritdoc/>
    public async Task<LiveMarkerDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(NormalizePath(filePath), cancellationToken).ConfigureAwait(false);
        var yaml = new YamlStream();

        try
        {
            yaml.Load(new StringReader(content));
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException("Invalid live marker YAML document.", exception);
        }

        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw InvalidField("document");
        }

        ValidateFields(root, ["StartTime", "Marker"]);
        var startTime = ReadDateTime(root, "StartTime");

        if (!root.Children.TryGetValue(new YamlScalarNode("Marker"), out var markerNode) ||
            markerNode is not YamlSequenceNode markers)
        {
            throw InvalidField("Marker");
        }

        var entries = new List<LiveMarkerEntry>(markers.Children.Count);

        foreach (var node in markers.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (node is not YamlMappingNode marker)
            {
                throw InvalidField("Marker");
            }

            ValidateFields(marker, ["RealDateTime", "LiveTime", "Message", "MarkerColor"]);
            var colorText = ReadScalar(marker, "MarkerColor");

            if (!Enum.GetNames<LiveMarkerColor>().Contains(colorText, StringComparer.Ordinal) ||
                !Enum.TryParse<LiveMarkerColor>(colorText, out var color))
            {
                throw InvalidField("MarkerColor");
            }

            entries.Add(new()
            {
                RealDateTime = ReadDateTime(marker, "RealDateTime"),
                LiveTime = ReadDuration(marker),
                Message = ReadScalar(marker, "Message"),
                MarkerColor = color
            });
        }

        return new() { StartTime = startTime, Marker = entries.ToArray() };
    }

    /// <inheritdoc/>
    public async Task SaveAsync(
        LiveMarkerDocument document,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        if (document.Marker is null)
        {
            throw InvalidField("Marker");
        }

        var snapshot = document with { Marker = document.Marker.ToArray() };
        var root = new YamlMappingNode
        {
            { "StartTime", snapshot.StartTime.ToString("O", CultureInfo.InvariantCulture) }
        };
        var markers = new YamlSequenceNode();

        foreach (var entry in snapshot.Marker)
        {
            if (entry is null || entry.LiveTime < TimeSpan.Zero || entry.Message is null || !Enum.IsDefined(entry.MarkerColor))
            {
                throw InvalidField("Marker");
            }

            markers.Add(new YamlMappingNode
            {
                { "RealDateTime", entry.RealDateTime.ToString("O", CultureInfo.InvariantCulture) },
                { "LiveTime", entry.LiveTime.ToString("c", CultureInfo.InvariantCulture) },
                { "Message", new YamlScalarNode(entry.Message) { Style = ScalarStyle.DoubleQuoted } },
                { "MarkerColor", entry.MarkerColor.ToString() }
            });
        }

        root.Add("Marker", markers);
        var yaml = new YamlStream(new YamlDocument(root));
        using var textWriter = new StringWriter(CultureInfo.InvariantCulture);
        yaml.Save(textWriter, false);
        var normalizedPath = NormalizePath(filePath);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(normalizedPath)!,
            $".{Path.GetFileName(normalizedPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(temporaryPath, new FileStreamOptions
                         {
                             Access = FileAccess.Write,
                             Mode = FileMode.CreateNew,
                             Share = FileShare.None,
                             Options = FileOptions.Asynchronous
                         }))
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                await writer.WriteAsync(textWriter.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, normalizedPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static DateTimeOffset ReadDateTime(YamlMappingNode node, string name)
    {
        var scalar = ReadScalar(node, name);

        if (!dateTimePattern.IsMatch(scalar) ||
            !DateTimeOffset.TryParse(scalar, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var result))
        {
            throw InvalidField(name);
        }

        return result;
    }

    private static TimeSpan ReadDuration(YamlMappingNode node)
    {
        var text = ReadScalar(node, "LiveTime");

        if (!text.StartsWith('-') &&
            TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out var legacy) && legacy >= TimeSpan.Zero)
        {
            return legacy;
        }

        var match = totalHoursPattern.Match(text);

        if (!match.Success ||
            !long.TryParse(match.Groups["hours"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(match.Groups["minutes"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(match.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            minutes >= 60 || seconds >= 60)
        {
            throw InvalidField("LiveTime");
        }

        try
        {
            var fractionText = match.Groups["fraction"].Value;
            var fraction = fractionText.Length == 0
                ? 0
                : int.Parse(fractionText.PadRight(7, '0'), CultureInfo.InvariantCulture);
            return TimeSpan.FromTicks(checked(hours * TimeSpan.TicksPerHour + minutes * TimeSpan.TicksPerMinute +
                seconds * TimeSpan.TicksPerSecond + fraction));
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Invalid live marker field: LiveTime.", exception);
        }
    }

    private static string ReadScalar(YamlMappingNode node, string name)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(name), out var value) || value is not YamlScalarNode scalar)
        {
            throw InvalidField(name);
        }

        return scalar.Value ?? "";
    }

    private static void ValidateFields(YamlMappingNode node, IReadOnlyList<string> names)
    {
        if (node.Children.Count != names.Count || node.Children.Keys.Any(key =>
                key is not YamlScalarNode scalar || scalar.Value is null || !names.Contains(scalar.Value, StringComparer.Ordinal)))
        {
            throw InvalidField(string.Join(", ", names));
        }
    }

    private static string NormalizePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return NormalizePath(filePath, 0);
    }

    private static string NormalizePath(string filePath, int resolvedLinks)
    {
        if (resolvedLinks >= 64)
        {
            throw new IOException("The live marker file path contains too many symbolic links.");
        }

        var fullPath = Path.GetFullPath(filePath);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;

        foreach (var segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo? target = null;

            if (Directory.Exists(current))
            {
                target = new DirectoryInfo(current).ResolveLinkTarget(true);
            }
            else if (File.Exists(current))
            {
                target = new FileInfo(current).ResolveLinkTarget(true);
            }

            if (target is not null)
            {
                current = NormalizePath(target.FullName, resolvedLinks + 1);
            }
        }

        return current;
    }

    private static InvalidDataException InvalidField(string name)
    {
        return new($"Invalid live marker field: {name}.");
    }
}
