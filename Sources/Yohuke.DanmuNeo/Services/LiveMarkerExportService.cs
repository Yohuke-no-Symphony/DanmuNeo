using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.LiveMarker;

namespace Yohuke.DanmuNeo.Services;

/// <summary>将直播记录的原始时间和颜色导出为文本或 Excel 工作簿。</summary>
public sealed class LiveMarkerExportService
{
    private const string SPREADSHEET_NAMESPACE = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>按直播内时间稳定排序，原子导出 UTF-8 制表符分隔文本。</summary>
    public async Task ExportTextAsync(LiveMarkerDocument document, string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = document.Marker.OrderBy(entry => entry.LiveTime).ToArray();
        var labels = CaptureLabels();

        await WriteAtomicallyAsync(filePath, async stream =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteLineAsync(string.Join('\t', GetHeaders(labels).Select(FlattenText)).AsMemory(),
                cancellationToken);
            foreach (var entry in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(string.Join('\t', GetValues(entry).Select(FlattenText)).AsMemory(),
                    cancellationToken);
            }
            await writer.FlushAsync(cancellationToken);
        }, cancellationToken);
    }

    /// <summary>按直播内时间稳定排序，原子导出保留七色行背景的 Excel 工作簿。</summary>
    public async Task ExportExcelAsync(LiveMarkerDocument document, string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        var rows = document.Marker.OrderBy(entry => entry.LiveTime).ToArray();
        var labels = CaptureLabels();

        await WriteAtomicallyAsync(filePath, stream => Task.Run(() =>
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, true);
            WritePackageEntries(archive, labels["LiveMarker.Export.Sheet"], cancellationToken);
            WriteXmlEntry(archive, "xl/styles.xml", WriteStyles);
            WriteXmlEntry(archive, "xl/worksheets/sheet1.xml", writer =>
                WriteWorksheet(writer, rows, labels, cancellationToken));
        }, cancellationToken), cancellationToken);
    }

    private static IReadOnlyDictionary<string, string> CaptureLabels()
    {
        return Localization.CaptureSnapshot(
        [
            "LiveMarker.Export.LiveTime", "LiveMarker.Export.RealTime", "LiveMarker.Export.Message",
            "LiveMarker.Export.Color", "LiveMarker.Export.Sheet"
        ]).Strings;
    }

    private static string[] GetHeaders(IReadOnlyDictionary<string, string> labels)
    {
        return
        [
            labels["LiveMarker.Export.LiveTime"], labels["LiveMarker.Export.RealTime"],
            labels["LiveMarker.Export.Message"], labels["LiveMarker.Export.Color"]
        ];
    }

    private static string[] GetValues(LiveMarkerEntry entry)
    {
        var time = entry.LiveTime;
        var hours = Math.Abs(time.Ticks / TimeSpan.TicksPerHour);
        var sign = time.Ticks < 0 ? "-" : string.Empty;
        var liveTime = string.Create(CultureInfo.InvariantCulture,
            $"{sign}{hours:00}:{Math.Abs(time.Minutes):00}:{Math.Abs(time.Seconds):00}.{Math.Abs(time.Milliseconds):000}");
        return
        [
            liveTime,
            entry.RealDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture),
            entry.Message,
            entry.MarkerColor.ToString()
        ];
    }

    private static string FlattenText(string value)
    {
        return value.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ')
            .Replace('\t', ' ');
    }

    private static async Task WriteAtomicallyAsync(string filePath, Func<Stream, Task> writeAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await writeAsync(stream);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destination, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WritePackageEntries(ZipArchive archive, string sheetName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WriteTextEntry(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """);
        WriteTextEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        WriteXmlEntry(archive, "xl/workbook.xml", writer =>
        {
            writer.WriteStartElement("workbook", SPREADSHEET_NAMESPACE);
            writer.WriteAttributeString("xmlns", "r", null,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            writer.WriteStartElement("sheets");
            writer.WriteStartElement("sheet");
            writer.WriteAttributeString("name", sheetName);
            writer.WriteAttributeString("sheetId", "1");
            writer.WriteAttributeString("r", "id",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "rId1");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        });
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string text)
    {
        using var stream = archive.CreateEntry(entryName).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static void WriteXmlEntry(ZipArchive archive, string entryName, Action<XmlWriter> write)
    {
        using var stream = archive.CreateEntry(entryName).Open();
        using var writer = XmlWriter.Create(stream, new()
        {
            Encoding = new UTF8Encoding(false),
            CloseOutput = false
        });
        writer.WriteStartDocument();
        write(writer);
        writer.WriteEndDocument();
    }

    private static void WriteWorksheet(XmlWriter writer, IReadOnlyList<LiveMarkerEntry> rows,
        IReadOnlyDictionary<string, string> labels, CancellationToken cancellationToken)
    {
        writer.WriteStartElement("worksheet", SPREADSHEET_NAMESPACE);
        writer.WriteStartElement("cols");
        var widths = new[] { 22, 34, 70, 14 };
        for (var index = 0; index < widths.Length; index++)
        {
            writer.WriteStartElement("col");
            writer.WriteAttributeString("min", (index + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("max", (index + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("width", widths[index].ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteStartElement("sheetData");
        WriteRow(writer, 1, GetHeaders(labels), 1);
        for (var index = 0; index < rows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = rows[index];
            WriteRow(writer, index + 2, GetValues(entry), GetColorStyle(entry.MarkerColor));
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void WriteRow(XmlWriter writer, int rowIndex, IReadOnlyList<string> values, int style)
    {
        writer.WriteStartElement("row");
        writer.WriteAttributeString("r", rowIndex.ToString(CultureInfo.InvariantCulture));
        for (var index = 0; index < values.Count; index++)
        {
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", $"{(char)('A' + index)}{rowIndex}");
            writer.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is");
            writer.WriteStartElement("t");
            writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
            writer.WriteString(EncodeSpreadsheetText(values[index]));
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static string EncodeSpreadsheetText(string value)
    {
        var text = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '_' && index + 6 < value.Length && value[index + 1] == 'x' &&
                value[index + 6] == '_' && value.AsSpan(index + 2, 4).ContainsAnyExcept("0123456789abcdefABCDEF") == false)
            {
                text.Append("_x005F_");
            }
            else if (char.IsHighSurrogate(character) && index + 1 < value.Length &&
                     char.IsLowSurrogate(value[index + 1]))
            {
                text.Append(character);
                text.Append(value[++index]);
            }
            else if (!XmlConvert.IsXmlChar(character) || character == '\r')
            {
                text.Append("_x").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture)).Append('_');
            }
            else
            {
                text.Append(character);
            }
        }
        return text.ToString();
    }

    private static int GetColorStyle(LiveMarkerColor color)
    {
        return color switch
        {
            LiveMarkerColor.Red => 2,
            LiveMarkerColor.Orange => 3,
            LiveMarkerColor.Yellow => 4,
            LiveMarkerColor.Green => 5,
            LiveMarkerColor.Blue => 6,
            LiveMarkerColor.Magenta => 7,
            _ => 8
        };
    }

    private static void WriteStyles(XmlWriter writer)
    {
        writer.WriteStartElement("styleSheet", SPREADSHEET_NAMESPACE);
        writer.WriteStartElement("fonts");
        writer.WriteAttributeString("count", "2");
        WriteFont(writer, false);
        WriteFont(writer, true);
        writer.WriteEndElement();
        writer.WriteStartElement("fills");
        writer.WriteAttributeString("count", "10");
        WriteFill(writer, "none", null);
        WriteFill(writer, "gray125", null);
        foreach (var color in new[]
                 {
                     "FF334155", "FFFEE2E2", "FFFFEDD5", "FFFEF9C3", "FFDCFCE7", "FFDBEAFE", "FFFAE8FF", "FFF1F5F9"
                 })
        {
            WriteFill(writer, "solid", color);
        }
        writer.WriteEndElement();
        writer.WriteStartElement("borders");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("border");
        foreach (var edge in new[] { "left", "right", "top", "bottom", "diagonal" })
        {
            writer.WriteElementString(edge, string.Empty);
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("cellStyleXfs");
        writer.WriteAttributeString("count", "1");
        WriteCellStyle(writer, 0, 0);
        writer.WriteEndElement();
        writer.WriteStartElement("cellXfs");
        writer.WriteAttributeString("count", "9");
        WriteCellStyle(writer, 0, 0);
        WriteCellStyle(writer, 1, 2);
        for (var fill = 3; fill < 10; fill++)
        {
            WriteCellStyle(writer, 0, fill);
        }
        writer.WriteEndElement();
        writer.WriteStartElement("cellStyles");
        writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("cellStyle");
        writer.WriteAttributeString("name", "Normal");
        writer.WriteAttributeString("xfId", "0");
        writer.WriteAttributeString("builtinId", "0");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteFont(XmlWriter writer, bool header)
    {
        writer.WriteStartElement("font");
        writer.WriteStartElement("sz");
        writer.WriteAttributeString("val", "11");
        writer.WriteEndElement();
        writer.WriteStartElement("name");
        writer.WriteAttributeString("val", "Calibri");
        writer.WriteEndElement();
        if (header)
        {
            writer.WriteElementString("b", string.Empty);
            writer.WriteStartElement("color");
            writer.WriteAttributeString("rgb", "FFFFFFFF");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteFill(XmlWriter writer, string pattern, string? color)
    {
        writer.WriteStartElement("fill");
        writer.WriteStartElement("patternFill");
        writer.WriteAttributeString("patternType", pattern);
        if (color is not null)
        {
            writer.WriteStartElement("fgColor");
            writer.WriteAttributeString("rgb", color);
            writer.WriteEndElement();
            writer.WriteStartElement("bgColor");
            writer.WriteAttributeString("indexed", "64");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteCellStyle(XmlWriter writer, int font, int fill)
    {
        writer.WriteStartElement("xf");
        writer.WriteAttributeString("numFmtId", "0");
        writer.WriteAttributeString("fontId", font.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("fillId", fill.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("borderId", "0");
        writer.WriteAttributeString("applyFont", "1");
        writer.WriteAttributeString("applyFill", "1");
        writer.WriteAttributeString("applyAlignment", "1");
        writer.WriteStartElement("alignment");
        writer.WriteAttributeString("vertical", "top");
        writer.WriteAttributeString("wrapText", "1");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}
