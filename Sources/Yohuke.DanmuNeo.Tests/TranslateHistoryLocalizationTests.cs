using System.IO.Compression;
using System.Xml.Linq;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证导出使用开始时的语言和记录快照，保留原始业务内容。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class TranslateHistoryLocalizationTests
{
    /// <summary>
    /// 导出开始后改变语言和展示状态，工作簿仍使用同一次捕获的文案与记录。
    /// </summary>
    [AvaloniaTheory]
    [InlineData("zh-CN", "en-US")]
    [InlineData("en-US", "zh-CN")]
    public async Task ExportKeepsItsInitialLanguageAndRecordSnapshot(string languageID, string nextLanguageID)
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage(languageID);
        var directory = Path.Combine(Path.GetTempPath(), "DanmuNeo-export-language-" + Guid.NewGuid().ToString("N"));
        var row = new DanmuFeedItem { Content = "原始用户正文 <&> / user content" };
        row.SetLocalizedStatus("Services.Send.Accepted");
        var originalStatus = row.Status;
        var expectedSheetName = Localization.Get("Services.Export.History");
        var expectedHeaders = new[] { "Time", "Status", "Content" }
            .Select(name => Localization.Get("Services.Export." + name)).ToArray();

        try
        {
            var export = new TranslateHistoryExportService().ExportToFileAsync([row], Path.Combine(directory, "history.xlsx"));
            Localization.SetLanguage(nextLanguageID);
            row.RefreshLocalization();
            var path = await export;
            using var archive = ZipFile.OpenRead(path);
            using var workbookStream = archive.GetEntry("xl/workbook.xml")!.Open();
            var workbook = XDocument.Load(workbookStream);
            XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Assert.Equal(expectedSheetName, workbook.Descendants(spreadsheet + "sheet").Single().Attribute("name")!.Value);
            using var worksheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var worksheet = XDocument.Load(worksheetStream);
            var rows = worksheet.Descendants(spreadsheet + "row").ToArray();
            Assert.Equal(expectedHeaders, rows[0].Descendants(spreadsheet + "t").Select(cell => cell.Value));
            var values = rows[1].Descendants(spreadsheet + "t").Select(cell => cell.Value).ToArray();
            Assert.Equal(originalStatus, values[1]);
            Assert.Equal("原始用户正文 <&> / user content", values[2]);
            Assert.NotEqual(row.Status, originalStatus);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
