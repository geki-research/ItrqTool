using System.Net;
using System.Text.Json;
using ItrqTool.Domain.Reporting;

namespace ItrqTool.Infrastructure.Reporting;

/// <summary>
/// Phase 2a placeholder writer. Emits a minimal HTML file containing the report
/// title and a JSON dump of the report data. Phase 2b replaces this with a
/// fully-rendered HTML report (HtmlGeneralDataDiffReportWriter).
/// </summary>
public sealed class StubHtmlGeneralDataDiffReportWriter : IHtmlGeneralDataDiffReportWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void WriteReport(HtmlDiffGeneralDataReportData data, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data, JsonOpts);

        var html =
            "<!DOCTYPE html>\n" +
            "<html lang=\"en\"><head><meta charset=\"utf-8\">\n" +
            $"<title>{WebUtility.HtmlEncode(data.Title)}</title>\n" +
            "<style>body{font:14px sans-serif;margin:24px;color:#1e293b}" +
            "pre{background:#f1f5f9;padding:12px;border-radius:6px;overflow:auto}" +
            ".banner{background:#fef3c7;border-left:4px solid #f59e0b;padding:10px 14px;margin-bottom:18px;border-radius:4px}" +
            "</style></head><body>\n" +
            $"<h1>{WebUtility.HtmlEncode(data.Title)}</h1>\n" +
            "<div class=\"banner\"><strong>Phase 2a placeholder.</strong> " +
            "This file is a stub; full HTML rendering with per-cell diff display is delivered by Phase 2b. " +
            "The report data is dumped as JSON below.</div>\n" +
            $"<p><strong>Previous workbook:</strong> {WebUtility.HtmlEncode(data.PreviousWorkbookPath)}</p>\n" +
            $"<p><strong>Current workbook:</strong> {WebUtility.HtmlEncode(data.CurrentWorkbookPath)}</p>\n" +
            $"<p><strong>Generated at:</strong> {data.GeneratedAt:u}</p>\n" +
            $"<p><strong>Counts:</strong> " +
              $"{data.Added.Count} added, {data.Removed.Count} removed, " +
              $"{data.Changed.Count} changed, {data.Unchanged.Count} unchanged.</p>\n" +
            "<h2>Report data (JSON)</h2>\n" +
            $"<pre>{WebUtility.HtmlEncode(json)}</pre>\n" +
            "</body></html>\n";

        File.WriteAllText(filePath, html);
    }
}
