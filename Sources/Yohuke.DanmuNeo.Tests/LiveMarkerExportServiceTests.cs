using System.IO.Compression;
using System.Threading;
using System.Xml.Linq;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证记录导出保留独立时间、稳定顺序、颜色和原文件。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class LiveMarkerExportServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "danmu-marker-export-" + Guid.NewGuid().ToString("N"));

    /// <summary>文本保留超过一天的累计小时、实际时间偏移和相同打点时间的原始顺序。</summary>
    [Fact]
    public async Task TextExportPreservesIndependentTimesAndStableOrderWithFlattenedMessages()
    {
        var firstRealTime = new DateTimeOffset(2020, 1, 2, 3, 4, 5, 123, TimeSpan.FromHours(-5));
        var laterRealTime = new DateTimeOffset(2030, 6, 7, 8, 9, 10, 456, TimeSpan.FromHours(9));
        var laterLiveTime = TimeSpan.FromHours(49) + new TimeSpan(0, 0, 2, 3, 4);
        var document = new LiveMarkerDocument
        {
            StartTime = new(2026, 10, 20, 10, 0, 0, TimeSpan.FromHours(8)),
            Marker =
            [
                new() { LiveTime = laterLiveTime, RealDateTime = laterRealTime, Message = "后\t来\r\n换\n行\r尾", MarkerColor = LiveMarkerColor.Blue },
                new() { LiveTime = TimeSpan.FromMilliseconds(1234), RealDateTime = firstRealTime, Message = "第一个", MarkerColor = LiveMarkerColor.Red },
                new() { LiveTime = TimeSpan.FromMilliseconds(1234), RealDateTime = laterRealTime, Message = "同时间第二个", MarkerColor = LiveMarkerColor.Green }
            ]
        };
        var originals = document.Marker.ToArray();
        var path = Path.Combine(directory, "records.txt");

        await new LiveMarkerExportService().ExportTextAsync(document, path);

        var rows = await File.ReadAllLinesAsync(path);
        Assert.Equal(GetHeaders(), rows[0].Split('\t'));
        Assert.Equal("00:00:01.234\t2020-01-02 03:04:05.123 -05:00\t第一个\tRed", rows[1]);
        Assert.Equal("00:00:01.234\t2030-06-07 08:09:10.456 +09:00\t同时间第二个\tGreen", rows[2]);
        Assert.Equal("49:02:03.004\t2030-06-07 08:09:10.456 +09:00\t后 来 换 行 尾\tBlue", rows[3]);
        Assert.Equal(originals, document.Marker);
        Assert.Equal(laterRealTime, document.Marker[0].RealDateTime);
        Assert.Equal(laterLiveTime, document.Marker[0].LiveTime);
        Assert.Equal(4, rows.Length);
    }

    /// <summary>Excel 使用文字单元格保存中文、特殊字符和原始实际时间，不将记录内容解释为公式。</summary>
    [Fact]
    public async Task ExcelExportPreservesTimesTextAndSafeXmlWithoutFormulas()
    {
        var document = new LiveMarkerDocument
        {
            StartTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Marker =
            [
                new()
                {
                    LiveTime = TimeSpan.FromHours(123) + TimeSpan.FromMilliseconds(456),
                    RealDateTime = new(2026, 10, 20, 12, 13, 14, 789, TimeSpan.FromHours(8)),
                    Message = " =SUM(A1:A2) <字幕>&\"中文\"\n第二行\t尾部 😀 \u0001 _x0041_\r",
                    MarkerColor = LiveMarkerColor.Magenta
                }
            ]
        };
        var path = Path.Combine(directory, "records.xlsx");

        await new LiveMarkerExportService().ExportExcelAsync(document, path);

        using var archive = ZipFile.OpenRead(path);
        var sheet = ReadPart(archive, "xl/worksheets/sheet1.xml");
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = sheet.Descendants(ns + "row").ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(GetHeaders(), ReadValues(rows[0]));
        Assert.Equal(
        [
            "123:00:00.456", "2026-10-20 12:13:14.789 +08:00",
            " =SUM(A1:A2) <字幕>&\"中文\"\n第二行\t尾部 😀 _x0001_ _x005F_x0041__x000D_", "Magenta"
        ], ReadValues(rows[1]));
        Assert.All(sheet.Descendants(ns + "c"), cell => Assert.Equal("inlineStr", cell.Attribute("t")?.Value));
        Assert.Empty(sheet.Descendants(ns + "f"));
        Assert.All(sheet.Descendants(ns + "t"), text => Assert.Equal("preserve", text.Attribute(XNamespace.Xml + "space")?.Value));
        Assert.Equal(4, sheet.Descendants(ns + "col").Count());
        Assert.Contains(archive.Entries, entry => entry.FullName == "xl/styles.xml");
        var contentTypes = ReadPart(archive, "[Content_Types].xml");
        Assert.Contains(contentTypes.Root!.Elements(), element => element.Attribute("PartName")?.Value == "/xl/styles.xml");
        var relations = ReadPart(archive, "xl/_rels/workbook.xml.rels");
        Assert.Contains(relations.Root!.Elements(), element => element.Attribute("Target")?.Value == "styles.xml");
        var workbook = ReadPart(archive, "xl/workbook.xml");
        Assert.Equal(Localization.Get("LiveMarker.Export.Sheet"), workbook.Descendants(ns + "sheet").Single().Attribute("name")?.Value);
    }

    /// <summary>七种颜色各自使用有效的浅色填充样式，同一行的所有列保持相同背景。</summary>
    [Fact]
    public async Task ExcelExportUsesSevenDistinctRowColorsAndKeepsEqualTimeOrder()
    {
        var colors = Enum.GetValues<LiveMarkerColor>();
        var document = new LiveMarkerDocument
        {
            Marker = colors.Select(color => new LiveMarkerEntry
            {
                LiveTime = TimeSpan.FromSeconds(10),
                RealDateTime = DateTimeOffset.UnixEpoch,
                Message = color.ToString(),
                MarkerColor = color
            }).ToArray()
        };
        var path = Path.Combine(directory, "colors.xlsx");

        await new LiveMarkerExportService().ExportExcelAsync(document, path);

        using var archive = ZipFile.OpenRead(path);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheet = ReadPart(archive, "xl/worksheets/sheet1.xml");
        var rows = sheet.Descendants(ns + "row").ToArray();
        Assert.Equal(colors.Length + 1, rows.Length);
        var styles = ReadPart(archive, "xl/styles.xml");
        var formats = styles.Root!.Element(ns + "cellXfs")!.Elements(ns + "xf").ToArray();
        var fills = styles.Root.Element(ns + "fills")!.Elements(ns + "fill").ToArray();
        var expectedColors = new[] { "FFFEE2E2", "FFFFEDD5", "FFFEF9C3", "FFDCFCE7", "FFDBEAFE", "FFFAE8FF", "FFF1F5F9" };
        Assert.All(rows[0].Elements(ns + "c"), cell => Assert.Equal("1", cell.Attribute("s")?.Value));
        for (var index = 0; index < colors.Length; index++)
        {
            var cells = rows[index + 1].Elements(ns + "c").ToArray();
            Assert.All(cells, cell => Assert.Equal((index + 2).ToString(), cell.Attribute("s")?.Value));
            Assert.Equal(colors[index].ToString(), ReadValues(rows[index + 1])[3]);
            var format = formats[index + 2];
            var fillIndex = int.Parse(format.Attribute("fillId")!.Value);
            Assert.Equal(expectedColors[index], fills[fillIndex].Descendants(ns + "fgColor").Single().Attribute("rgb")?.Value);
            Assert.Equal("1", format.Attribute("applyFill")?.Value);
        }
    }

    /// <summary>无记录时两种导出仍包含固定标题和有效的工作表。</summary>
    [Fact]
    public async Task EmptyDocumentExportsHeadersInBothFormats()
    {
        var service = new LiveMarkerExportService();
        var textPath = Path.Combine(directory, "empty.txt");
        var excelPath = Path.Combine(directory, "empty.xlsx");

        await service.ExportTextAsync(new(), textPath);
        await service.ExportExcelAsync(new(), excelPath);

        var lines = await File.ReadAllLinesAsync(textPath);
        Assert.Single(lines);
        Assert.Equal(GetHeaders(), lines[0].Split('\t'));
        using var archive = ZipFile.OpenRead(excelPath);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = ReadPart(archive, "xl/worksheets/sheet1.xml").Descendants(ns + "row").ToArray();
        Assert.Single(rows);
        Assert.Equal(GetHeaders(), ReadValues(rows[0]));
    }

    /// <summary>取消不会删除或覆盖用户已有文件，也不留下临时文件。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledExportPreservesExistingFile(bool excel)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, excel ? "existing.xlsx" : "existing.txt");
        var original = new byte[] { 0, 1, 2, 3, 4, 255 };
        await File.WriteAllBytesAsync(path, original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new LiveMarkerExportService();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => excel
            ? service.ExportExcelAsync(new(), path, cancellation.Token)
            : service.ExportTextAsync(new(), path, cancellation.Token));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFileSystemEntries(directory));
    }

    /// <summary>目标不能替换时保留已有目录及内容，清理本次导出的临时文件。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReplacementPreservesExistingDestinationAndCleansTemporaryFile(bool excel)
    {
        var path = Path.Combine(directory, "destination");
        Directory.CreateDirectory(path);
        var existingFile = Path.Combine(path, "keep.txt");
        await File.WriteAllTextAsync(existingFile, "原始内容");
        var service = new LiveMarkerExportService();

        await Assert.ThrowsAnyAsync<IOException>(() => excel
            ? service.ExportExcelAsync(new(), path)
            : service.ExportTextAsync(new(), path));

        Assert.Equal("原始内容", await File.ReadAllTextAsync(existingFile));
        Assert.Single(Directory.GetFileSystemEntries(directory));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private static XDocument ReadPart(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static string[] ReadValues(XElement row)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return row.Elements(ns + "c").Select(cell => cell.Element(ns + "is")!.Element(ns + "t")!.Value).ToArray();
    }

    private static string[] GetHeaders()
    {
        return
        [
            Localization.Get("LiveMarker.Export.LiveTime"), Localization.Get("LiveMarker.Export.RealTime"),
            Localization.Get("LiveMarker.Export.Message"), Localization.Get("LiveMarker.Export.Color")
        ];
    }
}
