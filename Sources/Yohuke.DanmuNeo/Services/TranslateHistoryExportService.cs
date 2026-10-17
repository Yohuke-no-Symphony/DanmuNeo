using System.IO.Compression;
using System.Xml;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 同传历史导出服务。
/// </summary>
public class TranslateHistoryExportService
{
    /// <summary>
    /// 导出同传历史为 Excel 文件。
    /// </summary>
    public async Task<string> ExportAsync(
        IEnumerable<DanmuFeedItem> items,
        string outputDirectory,
        string fileNamePrefix,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var safePrefix = SanitizeFileName(string.IsNullOrWhiteSpace(fileNamePrefix) ? "translate-history" : fileNamePrefix);
        var filePath = Path.Combine(outputDirectory, $"{safePrefix}-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");

        return await ExportToFileAsync(items, filePath, cancellationToken);
    }

    /// <summary>
    /// 导出同传历史到指定 Excel 文件。
    /// </summary>
    public async Task<string> ExportToFileAsync(
        IEnumerable<DanmuFeedItem> items,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        var rows = items
            .OrderBy(item => item.Time)
            .Select(item => (item.Time, item.Status, item.Content))
            .ToList();
        var labels = Localization.CaptureSnapshot(
        [
            "Services.Export.History", "Services.Export.Time", "Services.Export.Status", "Services.Export.Content"
        ]).Strings;

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            AddTextEntry(archive, "[Content_Types].xml", CreateContentTypesXml());
            AddTextEntry(archive, "_rels/.rels", CreateRootRelsXml());
            AddTextEntry(archive, "xl/workbook.xml", CreateWorkbookXml(labels["Services.Export.History"]));
            AddTextEntry(archive, "xl/_rels/workbook.xml.rels", CreateWorkbookRelsXml());
            AddTextEntry(archive, "xl/worksheets/sheet1.xml", CreateWorksheetXml(rows, labels));
        }, cancellationToken);

        return filePath;
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string text)
    {
        var entry = archive.CreateEntry(entryName);

        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(text);
    }

    private static string CreateContentTypesXml()
    {
        return """
               <?xml version="1.0" encoding="UTF-8"?>
               <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                 <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                 <Default Extension="xml" ContentType="application/xml"/>
                 <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                 <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
               </Types>
               """;
    }

    private static string CreateRootRelsXml()
    {
        return """
               <?xml version="1.0" encoding="UTF-8"?>
               <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                 <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
               </Relationships>
               """;
    }

    private static string CreateWorkbookXml(string sheetName)
    {
        return """
               <?xml version="1.0" encoding="UTF-8"?>
               <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                 <sheets>
                   <sheet name="__SHEET_NAME__" sheetId="1" r:id="rId1"/>
                 </sheets>
               </workbook>
               """.Replace("__SHEET_NAME__", System.Security.SecurityElement.Escape(
                   sheetName), StringComparison.Ordinal);
    }

    private static string CreateWorkbookRelsXml()
    {
        return """
               <?xml version="1.0" encoding="UTF-8"?>
               <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                 <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
               </Relationships>
               """;
    }

    private static string CreateWorksheetXml(
        IReadOnlyList<(DateTimeOffset Time, string Status, string Content)> rows,
        IReadOnlyDictionary<string, string> labels)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new()
               {
                   Encoding = System.Text.Encoding.UTF8,
                   Indent = true
               }))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            writer.WriteStartElement("sheetData");
            WriteRow(writer, 1,
            [
                labels["Services.Export.Time"],
                labels["Services.Export.Status"],
                labels["Services.Export.Content"]
            ]);

            for (var index = 0; index < rows.Count; index++)
            {
                var item = rows[index];
                WriteRow(writer, index + 2,
                [
                    item.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    item.Status,
                    item.Content
                ]);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteRow(XmlWriter writer, int rowIndex, IReadOnlyList<string> values)
    {
        writer.WriteStartElement("row");
        writer.WriteAttributeString("r", rowIndex.ToString());

        for (var index = 0; index < values.Count; index++)
        {
            writer.WriteStartElement("c");
            writer.WriteAttributeString("r", $"{GetColumnName(index)}{rowIndex}");
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is");
            writer.WriteElementString("t", values[index]);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static string GetColumnName(int index)
    {
        return ((char)('A' + index)).ToString();
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = value.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray();
        return new(chars);
    }
}
