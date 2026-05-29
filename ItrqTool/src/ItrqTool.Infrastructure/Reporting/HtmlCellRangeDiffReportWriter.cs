using System.Text;
using System.Text.Json;
using ItrqTool.Domain.Reporting;

namespace ItrqTool.Infrastructure.Reporting;

public sealed class HtmlCellRangeDiffReportWriter : IHtmlCellRangeDiffReportWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void WriteReport(HtmlDiffCellRangeReportData data, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data, JsonOpts);
        var html = BuildHtml(data, json);
        File.WriteAllText(filePath, html, Encoding.UTF8);
    }

    private static string BuildHtml(HtmlDiffCellRangeReportData data, string embeddedJson)
    {
        var file1 = HtmlEncode(Path.GetFileName(data.File1Path));
        var file2 = HtmlEncode(Path.GetFileName(data.File2Path));
        var sheet1 = HtmlEncode(data.Sheet1Name);
        var sheet2 = HtmlEncode(data.Sheet2Name);
        var generated = data.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var title = HtmlEncode(data.Title);
        var includeVF = data.IncludeValidationFormatting;

        var dvCfHeaderCols = includeVF
            ? "<th>File 1 DV</th><th>File 2 DV</th><th>File 1 CF</th><th>File 2 CF</th>"
            : "";

        var changedRows = BuildChangedRows(data.Changed, includeVF);
        var unchangedRows = BuildUnchangedRows(data.Unchanged, includeVF);

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1.0"/>
<title>{{title}}</title>
<style>
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; font-size: 14px; color: #1e293b; background: #f8fafc; padding: 24px; }
h1 { font-size: 22px; font-weight: 700; margin-bottom: 8px; }
h2 { font-size: 16px; font-weight: 600; margin: 24px 0 10px; }
.meta { color: #64748b; font-size: 13px; margin-bottom: 16px; }
.meta span { display: block; }
.cards { display: flex; gap: 16px; margin: 16px 0 24px; flex-wrap: wrap; }
.card { flex: 1; min-width: 120px; padding: 14px 18px; border-radius: 8px; border-left: 4px solid; background: #fff; box-shadow: 0 1px 3px rgba(0,0,0,.08); }
.card .count { font-size: 26px; font-weight: 700; line-height: 1; }
.card .label { font-size: 12px; color: #64748b; margin-top: 4px; text-transform: uppercase; letter-spacing: .04em; }
.card-changed   { border-color: #d97706; } .card-changed .count   { color: #d97706; }
.card-unchanged { border-color: #9ca3af; background: #f3f4f6; } .card-unchanged .count { color: #374151; }
table { width: 100%; border-collapse: collapse; background: #fff; border-radius: 8px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,.08); margin-bottom: 24px; font-size: 13px; }
th { background: #f1f5f9; text-align: left; padding: 10px 12px; font-weight: 600; border-bottom: 2px solid #e2e8f0; white-space: nowrap; }
td { padding: 8px 12px; border-bottom: 1px solid #f1f5f9; vertical-align: top; }
tr:last-child td { border-bottom: none; }
.hl-text { background: #fef3c7; }
.hl-dv   { background: #ede9fe; }
.hl-cf   { background: #fce7f3; }
.addr { font-family: monospace; font-weight: 600; color: #475569; }
.empty-note { color: #94a3b8; font-style: italic; padding: 16px; }
</style>
</head>
<body>
<h1>{{title}}</h1>
<div class="meta">
  <span>File 1: {{file1}} — Sheet: {{sheet1}}</span>
  <span>File 2: {{file2}} — Sheet: {{sheet2}}</span>
  <span>Generated: {{generated}}</span>
</div>
<div class="cards">
  <div class="card card-changed"><div class="count">{{data.Changed.Count}}</div><div class="label">Changed</div></div>
  <div class="card card-unchanged"><div class="count">{{data.Unchanged.Count}}</div><div class="label">Unchanged</div></div>
</div>
<h2>Changed cells</h2>
{{(data.Changed.Count == 0 ? "<p class=\"empty-note\">No changed cells.</p>" : $"<table><thead><tr><th>Address</th><th>File 1</th><th>File 2</th>{dvCfHeaderCols}</tr></thead><tbody>{changedRows}</tbody></table>")}}
<h2>Unchanged cells</h2>
{{(data.Unchanged.Count == 0 ? "<p class=\"empty-note\">No unchanged cells.</p>" : $"<table><thead><tr><th>Address</th><th>Value</th>{(includeVF ? "<th>DV</th><th>CF</th>" : "")}</tr></thead><tbody>{unchangedRows}</tbody></table>")}}
<script id="reportData" type="application/json">
{{embeddedJson}}
</script>
</body>
</html>
""";
    }

    private static string BuildChangedRows(
        IReadOnlyList<HtmlDiffCellRangeChangedCell> cells, bool includeVF)
    {
        var sb = new StringBuilder();
        foreach (var c in cells)
        {
            var textCls   = c.TextChanged ? " class=\"hl-text\"" : "";
            var dvCls     = c.DvChanged   ? " class=\"hl-dv\""   : "";
            var cfCls     = c.CfChanged   ? " class=\"hl-cf\""   : "";
            var dvCfCells = includeVF
                ? $"<td{dvCls}>{HtmlEncode(c.File1DvDisplay)}</td><td{dvCls}>{HtmlEncode(c.File2DvDisplay)}</td>" +
                  $"<td{cfCls}>{HtmlEncode(c.File1CfDisplay)}</td><td{cfCls}>{HtmlEncode(c.File2CfDisplay)}</td>"
                : "";
            sb.Append(
                $"<tr><td class=\"addr\">{HtmlEncode(c.Address)}</td>" +
                $"<td{textCls}>{HtmlEncode(c.File1Value)}</td>" +
                $"<td{textCls}>{HtmlEncode(c.File2Value)}</td>" +
                $"{dvCfCells}</tr>");
        }
        return sb.ToString();
    }

    private static string BuildUnchangedRows(
        IReadOnlyList<HtmlDiffCellRangeUnchangedCell> cells, bool includeVF)
    {
        var sb = new StringBuilder();
        foreach (var u in cells)
        {
            var dvCfCells = includeVF
                ? $"<td>{HtmlEncode(u.DvDisplay)}</td><td>{HtmlEncode(u.CfDisplay)}</td>"
                : "";
            sb.Append(
                $"<tr><td class=\"addr\">{HtmlEncode(u.Address)}</td>" +
                $"<td>{HtmlEncode(u.Value)}</td>" +
                $"{dvCfCells}</tr>");
        }
        return sb.ToString();
    }

    private static string HtmlEncode(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
    }
}
